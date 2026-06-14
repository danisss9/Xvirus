using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using Xvirus.Model;

namespace Xvirus
{
    public class Scanner
    {
        public readonly SettingsDTO settings;
        private readonly DB database;
        private readonly AI ai;
        private readonly Rules rules;

        // Registry of in-progress scans, keyed by the file or folder path being scanned, so
        // concurrent scans of different paths each have an independent CancellationTokenSource and
        // can be cancelled individually via CancelScan(path). Starting a new scan of a path that is
        // already being scanned cancels and replaces the in-progress one.
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _activeScans = new();

        public Scanner(SettingsDTO settings, DB database, AI ai, Rules rules)
        {
            this.settings = settings;
            this.database = database;
            this.ai = ai;
            this.rules = rules;
        }

        private CancellationTokenSource RegisterScan(string path)
        {
            var cts = new CancellationTokenSource();
            _activeScans.AddOrUpdate(path, cts, (_, existing) =>
            {
                // A scan of this path is already running; cancel and replace it.
                try { existing.Cancel(); } catch (ObjectDisposedException) { }
                existing.Dispose();
                return cts;
            });
            return cts;
        }

        private void UnregisterScan(string path, CancellationTokenSource owned)
        {
            // Only remove if the registered cts is still ours (not replaced by a newer same-path scan).
            _activeScans.TryRemove(new KeyValuePair<string, CancellationTokenSource>(path, owned));
            owned.Dispose();
        }

        /// <summary>
        /// Cancels the in-progress scan of the file or folder at <paramref name="path"/>.
        /// Returns <c>true</c> if a matching scan was signalled to cancel, <c>false</c> otherwise.
        /// Safe to call from another thread while a scan is running.
        /// </summary>
        public bool CancelScan(string path)
        {
            if (path != null && _activeScans.TryGetValue(path, out var cts))
            {
                try { cts.Cancel(); return true; }
                catch (ObjectDisposedException) { return false; }
            }
            return false;
        }

        /// <summary>
        /// Cancels every in-progress scan on this scanner. Returns the number of scans signalled
        /// to cancel. Safe to call from another thread.
        /// </summary>
        public int CancelAllScans()
        {
            int count = 0;
            foreach (var kvp in _activeScans)
            {
                try { kvp.Value.Cancel(); count++; }
                catch (ObjectDisposedException) { }
            }
            return count;
        }

        /// <summary>
        /// Scans a single file. The scan is registered under <paramref name="filePath"/> and can be
        /// cancelled while in progress via <see cref="CancelScan(string)"/>. Starting another scan of
        /// the same path cancels and replaces the in-progress one.
        /// </summary>
        public ScanResult ScanFile(string filePath)
        {
            var cts = RegisterScan(filePath);
            try { return ScanFile(filePath, cts.Token); }
            finally { UnregisterScan(filePath, cts); }
        }

        public ScanResult ScanFile(string filePath, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            var fileInfo = new FileInfo(filePath);
            if (!fileInfo.Exists)
                return new ScanResult(-1, "File not found!", filePath);

            if (settings.MaxScanLength != null && fileInfo.Length > settings.MaxScanLength)
                return new ScanResult(-1, "File too big!", filePath);

            var rule = rules.GetRuleType(filePath);
            if (rule == RuleType.Block)
                return new ScanResult(1, "Blocked", filePath);
            else if (rule == RuleType.Allow)
                return new ScanResult(0, "Safe", filePath);

            ct.ThrowIfCancellationRequested();

            string? hash = null;
            using (var md5 = MD5.Create())
            {
                using var stream = Utils.ReadFile(filePath, fileInfo.Length);
                var checksum = md5.ComputeHash(stream);
                hash = BitConverter.ToString(checksum).Replace("-", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
            }

            if (hash == null)
                return new ScanResult(-1, "Could not get file hash!", filePath);

            if (database.safeHashList.Contains(hash))
                return new ScanResult(0, "Safe", filePath);

            if (settings.EnableSignatures && database.malHashList.Contains(hash))
                return new ScanResult(1, "Malware", filePath);

            ct.ThrowIfCancellationRequested();

            var certName = Utils.GetCertificateSubjectName(filePath);
            if (certName != null)
            {
                if (database.malVendorList.TryGetValue(certName, out string? value))
                {
                    return new ScanResult(1, value, filePath);
                }
                else
                {
                    return new ScanResult(0, "Safe", filePath);
                }
            }

            if (settings.EnableHeuristics || settings.EnableAIScan)
            {
                ct.ThrowIfCancellationRequested();

                bool isExecutable = false;
                using (var stream = File.OpenRead(filePath))
                {
                    using var reader = new BinaryReader(stream);
                    try
                    {
                        var bytes = reader.ReadChars(2);
                        isExecutable = bytes[0] == 'M' && bytes[1] == 'Z';
                    }
                    catch (ArgumentException) { }
                }

                if (settings.EnableHeuristics)
                {
                    if (isExecutable && database.heurListPatterns != null && (settings.MaxHeuristicsPeScanLength == null || fileInfo.Length <= settings.MaxHeuristicsPeScanLength))
                    {
                        using var stream = Utils.ReadFile(filePath, fileInfo.Length);
                        var matches = database.heurListPatterns.Search(stream, ct);
                        int score = 0;
                        foreach (var match in matches)
                        {
                            if (database.heurListDeps.TryGetValue(match.Key, out var matchDeps))
                            {
                                var matchesKeys = matches.Select(m => m.Key).ToHashSet();
                                if (matchDeps.All(dep => dep[0] == '!' ? !matchesKeys.Contains(dep.Substring(1)) : matchesKeys.Contains(dep)))
                                {
                                    var nameDeps = database.heurList[match.Key];
                                    return new ScanResult(1, nameDeps, filePath);
                                }
                            }

                            if (score < (5 - settings.HeuristicsLevel))
                            {
                                score += match.Key.StartsWith("Suspicious:") ? 1 : 2;
                                continue;
                            }

                            if (database.heurList.TryGetValue(match.Key, out var name))
                            {
                                return new ScanResult(1, name, filePath);
                            }
                        }
                    }
                    else if (!isExecutable && database.heurScriptListPatterns != null && (settings.MaxHeuristicsOthersScanLength == null || fileInfo.Length <= settings.MaxHeuristicsOthersScanLength)) // 10MBs
                    {
                        using var stream = Utils.ReadFile(filePath, fileInfo.Length);
                        var matches = database.heurScriptListPatterns.Search(stream, ct);
                        int score = 0;
                        foreach (var match in matches)
                        {
                            if (database.heurScriptListDeps.TryGetValue(match.Key, out var matchDeps))
                            {
                                var matchesKeys = matches.Select(m => m.Key).ToHashSet();
                                if (matchDeps.All(dep => dep[0] == '!' ? !matchesKeys.Contains(dep.Substring(1)) : matchesKeys.Contains(dep)))
                                {
                                    var nameDeps = database.heurScriptList[match.Key];
                                    return new ScanResult(1, nameDeps, filePath);
                                }
                            }

                            if (score < (5 - settings.HeuristicsLevel))
                            {
                                score += match.Key.StartsWith("Suspicious:") ? 1 : 2;
                                continue;
                            }

                            if (database.heurScriptList.TryGetValue(match.Key, out var name))
                            {
                                return new ScanResult(1, name, filePath);
                            }
                        }
                    }
                }

                if (settings.EnableAIScan && isExecutable && (settings.MaxAIScanLength == null || fileInfo.Length <= settings.MaxAIScanLength))
                {
                    ct.ThrowIfCancellationRequested();

                    var aiScore = ai.ScanFile(filePath);
                    return new ScanResult(aiScore, $"AI.{aiScore * 100:00.00}", filePath, (100 - (double)settings.AILevel) / 100);
                }
            }
            return new ScanResult(0, "Safe", filePath);
        }

        /// <summary>
        /// Scans a folder. The scan is registered under <paramref name="folderPath"/> and can be
        /// cancelled while in progress via <see cref="CancelScan(string)"/>. A cancelled scan stops
        /// gracefully and yields the results gathered so far.
        /// </summary>
        public IEnumerable<ScanResult> ScanFolder(string folderPath)
        {
            var cts = RegisterScan(folderPath);
            try
            {
                foreach (var result in ScanFolder(folderPath, cts.Token))
                    yield return result;
            }
            finally { UnregisterScan(folderPath, cts); }
        }

        public IEnumerable<ScanResult> ScanFolder(string folderPath, CancellationToken ct)
        {
            if (!Directory.Exists(folderPath))
                yield break;

            var filePaths = Directory.GetFiles(folderPath, "*", new EnumerationOptions() { RecurseSubdirectories = true, AttributesToSkip = 0 });

            foreach (var filePath in filePaths)
            {
                if (ct.IsCancellationRequested)
                    yield break;

                ScanResult result;
                try
                {
                    result = ScanFile(filePath, ct);
                }
                catch (OperationCanceledException)
                {
                    yield break;
                }

                yield return result;
            }
        }
    }
}
