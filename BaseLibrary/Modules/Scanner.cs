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
            return ScanFileInternal(filePath, ct, 0);
        }

        /// <summary>
        /// Single-file scan with optional recursive archive unpacking. <paramref name="archiveDepth"/>
        /// tracks how many archive layers we have descended through so nested archives cannot
        /// recurse unbounded.
        /// </summary>
        private ScanResult ScanFileInternal(string filePath, CancellationToken ct, int archiveDepth)
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

            // Determine once whether the file is a PE executable. This is reused by the
            // OnlyScanExecutables short-circuit below and by the heuristics/AI branches later,
            // avoiding a second header read.
            bool isExecutable = IsExecutable(filePath);

            // When OnlyScanExecutables is on, skip non-executables early — before the expensive
            // hash computation and database lookups. Archives are exempt when archive scanning is
            // enabled, since they may contain executables worth scanning.
            if (settings.OnlyScanExecutables && !isExecutable)
            {
                bool isArchive = settings.EnableArchiveScan && ArchiveExtractor.IsArchive(filePath);
                if (!isArchive)
                    return new ScanResult(0, "Safe", filePath);
            }

            string? hash = null;
            using (var md5 = MD5.Create())
            {
                using var stream = Utils.ReadFile(filePath, fileInfo.Length);
                var checksum = md5.ComputeHash(stream);
                hash = BitConverter.ToString(checksum).Replace("-", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
            }

            if (hash == null)
                return new ScanResult(-1, "Could not get file hash!", filePath);

            if (database.safeBloom?.MightContain(hash) != false && database.safeHashList.Contains(hash))
                return new ScanResult(0, "Safe", filePath);

            if (settings.EnableSignatures && database.malBloom?.MightContain(hash) != false && database.malHashList.Contains(hash))
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

                if (settings.EnableHeuristics)
                {
                    if (isExecutable && database.heurListPatterns != null && (settings.MaxHeuristicsPeScanLength == null || fileInfo.Length <= settings.MaxHeuristicsPeScanLength))
                    {
                        using var stream = Utils.ReadFile(filePath, fileInfo.Length);
                        var matches = database.heurListPatterns.Search(stream, ct).ToList();
                        var matchesKeys = matches.ToHashSet();
                        int score = 0;
                        foreach (var match in matches)
                        {
                            if (database.heurListDeps.TryGetValue(match, out var matchDeps))
                            {
                                if (matchDeps.All(dep => dep[0] == '!' ? !matchesKeys.Contains(dep.Substring(1)) : matchesKeys.Contains(dep)))
                                {
                                    var nameDeps = database.heurList[match];
                                    return new ScanResult(1, nameDeps, filePath);
                                }
                            }

                            if (score < (5 - settings.HeuristicsLevel))
                            {
                                score += match.StartsWith("Suspicious:") ? 1 : 2;
                                continue;
                            }

                            if (database.heurList.TryGetValue(match, out var name))
                            {
                                return new ScanResult(1, name, filePath);
                            }
                        }
                    }
                    else if (!isExecutable && database.heurScriptListPatterns != null && (settings.MaxHeuristicsOthersScanLength == null || fileInfo.Length <= settings.MaxHeuristicsOthersScanLength)) // 10MBs
                    {
                        using var stream = Utils.ReadFile(filePath, fileInfo.Length);
                        var matches = database.heurScriptListPatterns.Search(stream, ct).ToList();
                        var matchesKeys = matches.ToHashSet();
                        int score = 0;
                        foreach (var match in matches)
                        {
                            if (database.heurScriptListDeps.TryGetValue(match, out var matchDeps))
                            {
                                if (matchDeps.All(dep => dep[0] == '!' ? !matchesKeys.Contains(dep.Substring(1)) : matchesKeys.Contains(dep)))
                                {
                                    var nameDeps = database.heurScriptList[match];
                                    return new ScanResult(1, nameDeps, filePath);
                                }
                            }

                            if (score < (5 - settings.HeuristicsLevel))
                            {
                                score += match.StartsWith("Suspicious:") ? 1 : 2;
                                continue;
                            }

                            if (database.heurScriptList.TryGetValue(match, out var name))
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

                if (settings.EnableCloudScan)
                {
                    ct.ThrowIfCancellationRequested();

                    var cloudVerdict = CloudReputation.CheckHash(hash);
                    if (cloudVerdict == true) return new ScanResult(1, "Cloud.Suspicious", filePath);
                    if (cloudVerdict == false) return new ScanResult(0, "Safe", filePath);
                }
            }

            // The archive itself was not flagged by any engine; if archive scanning is enabled,
            // descend into it (depth-guarded) and scan every extracted entry. A malware hit on an
            // inner entry is reported against the outer archive path so callers quarantine the
            // archive, not the temp-extracted copy.
            if (settings.EnableArchiveScan && ArchiveExtractor.IsArchive(filePath))
            {
                var archiveResult = ScanArchiveContents(filePath, ct, archiveDepth);
                if (archiveResult != null)
                    return archiveResult;
            }

            return new ScanResult(0, "Safe", filePath);
        }

        /// <summary>
        /// Checks whether <paramref name="filePath"/> starts with the PE "MZ" magic bytes.
        /// Returns <c>false</c> on any read error or if the file is too short.
        /// </summary>
        private static bool IsExecutable(string filePath)
        {
            try
            {
                using var stream = File.OpenRead(filePath);
                using var reader = new BinaryReader(stream);
                var bytes = reader.ReadChars(2);
                return bytes.Length >= 2 && bytes[0] == 'M' && bytes[1] == 'Z';
            }
            catch (ArgumentException) { }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return false;
        }

        /// <summary>
        /// Extracts <paramref name="archivePath"/> to a temp directory and scans each entry via
        /// <see cref="ScanFileInternal"/> with an incremented depth. Returns <c>null</c> when no
        /// inner entry is malicious (so the caller reports the archive as Safe), or a malware
        /// <see cref="ScanResult"/> when one is. Returns a "Suspicious.ArchiveBomb" verdict when
        /// the archive trips the depth/size/count guards.
        /// </summary>
        private ScanResult? ScanArchiveContents(string archivePath, CancellationToken ct, int archiveDepth)
        {
            int maxDepth = settings.MaxArchiveDepth ?? ArchiveExtractor.DefaultMaxDepth;
            if (archiveDepth >= maxDepth)
                return null;

            var limits = new ArchiveExtractor.ArchiveLimits(
                maxDepth,
                (long)(settings.MaxArchiveTotalSize ?? ArchiveExtractor.DefaultMaxTotalSize),
                settings.MaxArchiveFileCount ?? ArchiveExtractor.DefaultMaxFileCount);

            string tempDir;
            try
            {
                tempDir = Path.Combine(Path.GetTempPath(), "xvirus-archive-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDir);
            }
            catch (Exception ex)
            {
                Logger.LogException(ex);
                return null;
            }

            try
            {
                List<string> extracted;
                try
                {
                    extracted = ArchiveExtractor.Extract(archivePath, tempDir, limits, ct);
                }
                catch (ArchiveExtractor.ArchiveBombException)
                {
                    return new ScanResult(1, "Suspicious.ArchiveBomb", archivePath);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // Corrupt or unsupported archive — log and treat the archive as non-malicious.
                    Logger.LogException(ex);
                    return null;
                }

                foreach (var innerPath in extracted)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        var innerResult = ScanFileInternal(innerPath, ct, archiveDepth + 1);
                        if (innerResult.IsMalware)
                        {
                            return new ScanResult(
                                innerResult.MalwareScore,
                                $"{innerResult.Name} (in {Path.GetFileName(archivePath)})",
                                archivePath);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        Logger.LogException(ex);
                    }
                }

                return null;
            }
            finally
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
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
