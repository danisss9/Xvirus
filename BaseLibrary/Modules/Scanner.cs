using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Xvirus.Model;

namespace Xvirus
{
    public class Scanner
    {
        public readonly SettingsDTO settings;
        private readonly DB database;
        private readonly AI ai;
        private readonly Rules rules;

        public Scanner(SettingsDTO settings, DB database, AI ai, Rules rules)
        {
            this.settings = settings;
            this.database = database;
            this.ai = ai;
            this.rules = rules;
        }

        public ScanResult ScanFile(string filePath)
        {
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
                    if (isExecutable && (settings.MaxHeuristicsPeScanLength == null || fileInfo.Length <= settings.MaxHeuristicsPeScanLength))
                    {
                        using var stream = Utils.ReadFile(filePath, fileInfo.Length);
                        var matches = database.heurListPatterns.Search(stream);
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
                    else if (!isExecutable && (settings.MaxHeuristicsOthersScanLength == null || fileInfo.Length <= settings.MaxHeuristicsOthersScanLength)) // 10MBs
                    {
                        using var stream = Utils.ReadFile(filePath, fileInfo.Length);
                        var matches = database.heurScriptListPatterns.Search(stream);
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
                    var aiScore = ai.ScanFile(filePath);
                    return new ScanResult(aiScore, $"AI.{aiScore * 100:00.00}", filePath, (100 - (double)settings.AILevel) / 100);
                }
            }
            return new ScanResult(0, "Safe", filePath);
        }

        public IEnumerable<ScanResult> ScanFolder(string folderPath)
        {
            if (!Directory.Exists(folderPath))
                yield break;

            var filePaths = Directory.GetFiles(folderPath, "*", new EnumerationOptions() { RecurseSubdirectories = true, AttributesToSkip = 0 });

            foreach (var filePath in filePaths)
            {
                yield return ScanFile(filePath);
            }
        }
    }
}
