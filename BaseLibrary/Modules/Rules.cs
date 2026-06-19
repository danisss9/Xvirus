using BaseLibrary.Serializers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Xvirus.Model;

namespace Xvirus
{
    public class Rules
    {
        private static readonly ReaderWriterLock rwl = new ReaderWriterLock();
        private Dictionary<string, Rule> _rules = new Dictionary<string, Rule>(StringComparer.OrdinalIgnoreCase);

        public Rules()
        {
            _rules = GetRules("rules.json");
        }

        public Rule AddAllowRule(string path, string rulesFilePath = "rules.json")
        {
            return AddRule(path, RuleType.Allow, rulesFilePath);
        }

        public Rule AddBlockRule(string path, string rulesFilePath = "rules.json")
        {
            return AddRule(path, RuleType.Block, rulesFilePath);
        }

        public Rule AddRule(string path, RuleType type, string rulesFilePath = "rules.json")
        {
            var rulesPath = Utils.RelativeToFullPath(rulesFilePath);
            Rule rule;

            rwl.AcquireWriterLock(2000);
            try
            {
                if (_rules.TryGetValue(path, out var existing))
                {
                    // Path already has a rule — flip its type and rewrite the file.
                    rule = existing;
                    rule.Type = type;
                    var allRulesJson = string.Join(Environment.NewLine, _rules.Values.Select(r => JsonSerializer.Serialize(r, SourceGenerationContext.Default.Rule)));
                    File.WriteAllText(rulesPath, allRulesJson + Environment.NewLine);
                }
                else
                {
                    rule = new Rule { Path = path, Type = type };
                    _rules.Add(rule.Path, rule);
                    var json = JsonSerializer.Serialize(rule, SourceGenerationContext.Default.Rule);
                    File.AppendAllText(rulesPath, json + Environment.NewLine);
                }
            }
            finally
            {
                rwl.ReleaseWriterLock();
            }

            ApplyEnforcement(rule);
            return rule;
        }

        public void RemoveRule(string id, string rulesFilePath = "rules.json")
        {
            var rulesPath = Utils.RelativeToFullPath(rulesFilePath);
            Rule? removed = null;
            rwl.AcquireWriterLock(2000);
            try
            {
                var match = _rules.FirstOrDefault(r => r.Value.Id == id);
                var path = match.Key;
                if (path != null && _rules.Remove(path))
                {
                    removed = match.Value;
                    // Rewrite the entire file without the removed rule
                    var allRulesJson = string.Join(Environment.NewLine, _rules.Values.Select(r => JsonSerializer.Serialize(r, SourceGenerationContext.Default.Rule)));
                    File.WriteAllText(rulesPath, allRulesJson + Environment.NewLine);
                }
            }
            finally
            {
                rwl.ReleaseWriterLock();
            }

            // Lift any active firewall block for the removed rule (firewall product only).
            if (removed?.Type == RuleType.Block && AppInfo.IsFirewall)
            {
                try { Firewall.UnblockProgram(removed.Path); }
                catch (Exception ex) { Logger.LogException(ex); }
            }
        }

        /// <summary>Re-applies every block rule to the OS firewall (firewall product only).</summary>
        public void SyncEnforcement()
        {
            if (!AppInfo.IsFirewall) return;

            List<Rule> blockRules;
            rwl.AcquireReaderLock(2000);
            try
            {
                blockRules = _rules.Values.Where(r => r.Type == RuleType.Block).ToList();
            }
            finally
            {
                rwl.ReleaseReaderLock();
            }

            foreach (var rule in blockRules)
                ApplyEnforcement(rule);
        }

        // Applies (or lifts) OS-level firewall enforcement for a single rule. No-op for
        // the anti-malware product, where rules stay advisory hints for the scanner.
        private static void ApplyEnforcement(Rule rule)
        {
            if (!AppInfo.IsFirewall) return;
            try
            {
                if (rule.Type == RuleType.Block)
                    Firewall.BlockProgram(rule.Path);
                else
                    Firewall.UnblockProgram(rule.Path);
            }
            catch (Exception ex)
            {
                Logger.LogException(ex);
            }
        }


        public RuleType? GetRuleType(string path)
        {
            return _rules.GetValueOrDefault(path)?.Type;
        }

        public List<Rule> GetAllRules()
        {
            return [.. _rules.Values];
        }

        private static Dictionary<string, Rule> GetRules(string rulesFilePath = "rules.json")
        {
            var result = new Dictionary<string, Rule>(StringComparer.OrdinalIgnoreCase);
            var path = Utils.RelativeToFullPath(rulesFilePath);
            try
            {
                if (File.Exists(path))
                {
                    foreach (var line in File.ReadAllLines(path))
                    {
                        try
                        {
                            var e = JsonSerializer.Deserialize(line, SourceGenerationContext.Default.Rule);
                            if (e != null)
                                result.Add(e.Path, e);
                        }
                        catch { /* ignore parse errors */ }
                    }
                }
                else
                {
                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var lines = new List<string>();

                    foreach (var process in System.Diagnostics.Process.GetProcesses())
                    {
                        try
                        {
                            var exePath = process.MainModule?.FileName;
                            if (string.IsNullOrEmpty(exePath) || !seen.Add(exePath))
                                continue;

                            var rule = new Rule { Path = exePath, Type = RuleType.Allow };
                            lines.Add(JsonSerializer.Serialize(rule, SourceGenerationContext.Default.Rule));
                            result.Add(rule.Path, rule);
                        }
                        catch { /* some processes deny access to MainModule */ }
                        finally { process.Dispose(); }
                    }

                    File.WriteAllText(path, string.Join(Environment.NewLine, lines) + Environment.NewLine);
                }
            }
            catch { /* swallow any I/O errors */ }
            return result;
        }
    }
}
