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

        // Tracks dynamically resolved IPs currently enforced for RuleKind.Domain block rules:
        // domain (lowercased) -> set of IP strings.
        private readonly Dictionary<string, HashSet<string>> _domainResolvedIps = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Global Windows Firewall profile applied to rules whose own profile is "any".</summary>
        public string ActiveProfile { get; set; } = "any";

        /// <summary>Fired after any rule is added, updated, or removed.</summary>
        public event Action<Rule>? RulesChanged;

        public Rules() : this("rules.json") { }

        public Rules(string rulesFilePath, bool seedRunningProcesses = true)
        {
            _rules = GetRules(rulesFilePath, seedRunningProcesses);
        }

        public static string BuildKey(RuleKind kind, string path, string protocol = "any", string direction = "both")
        {
            var normalizedPath = (path ?? string.Empty).Trim();
            var proto = Firewall.NormalizeProtocol(protocol);
            var dir = Firewall.NormalizeDirection(direction);
            if (kind == RuleKind.Program && proto == "any" && dir == "both")
                return normalizedPath;

            return $"{kind.ToString().ToLowerInvariant()}|{normalizedPath}|{proto}|{dir}";
        }

        private static string BuildKey(Rule rule) => BuildKey(rule.Kind, rule.Path, rule.Protocol, rule.Direction);

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
            return AddRule(new Rule
            {
                Kind = RuleKind.Program,
                Path = path,
                Type = type,
                Protocol = "any",
                Direction = "both",
                Profile = "any"
            }, rulesFilePath);
        }

        public Rule AddRule(Rule input, string rulesFilePath = "rules.json")
        {
            var rulesPath = Utils.RelativeToFullPath(rulesFilePath);
            var normalized = new Rule
            {
                Id = string.IsNullOrWhiteSpace(input.Id) ? Guid.NewGuid().ToString() : input.Id,
                Kind = input.Kind,
                Path = (input.Path ?? string.Empty).Trim(),
                Type = input.Type ?? RuleType.Block,
                Protocol = Firewall.NormalizeProtocol(input.Protocol),
                Direction = Firewall.NormalizeDirection(input.Direction),
                Profile = Firewall.NormalizeProfile(input.Profile)
            };

            var key = BuildKey(normalized);
            Rule rule;
            List<string>? domainIpsToRemove = null;

            rwl.AcquireWriterLock(2000);
            try
            {
                if (_rules.TryGetValue(key, out var existing))
                {
                    // Matching target already has a rule — update type/profile and rewrite.
                    rule = existing;
                    var wasBlock = rule.Type == RuleType.Block;
                    rule.Type = normalized.Type;
                    rule.Profile = normalized.Profile;

                    if (wasBlock && rule.Type != RuleType.Block && rule.Kind == RuleKind.Domain &&
                        _domainResolvedIps.TryGetValue(rule.Path, out var ips))
                    {
                        domainIpsToRemove = [.. ips];
                        _domainResolvedIps.Remove(rule.Path);
                    }

                    RewriteRulesLocked(rulesPath);
                }
                else
                {
                    rule = normalized;
                    _rules.Add(key, rule);
                    var json = JsonSerializer.Serialize(rule, SourceGenerationContext.Default.Rule);
                    File.AppendAllText(rulesPath, json + Environment.NewLine);
                }
            }
            finally
            {
                rwl.ReleaseWriterLock();
            }

            if (domainIpsToRemove != null && AppInfo.IsFirewall)
            {
                foreach (var ip in domainIpsToRemove)
                {
                    try { Firewall.DeleteRuleByName(Firewall.DomainIpRuleNameFor(rule.Path, ip)); }
                    catch (Exception ex) { Logger.LogException(ex); }
                }
            }

            ApplyEnforcement(rule);
            RulesChanged?.Invoke(rule);
            return rule;
        }

        public void RemoveRule(string id, string rulesFilePath = "rules.json")
        {
            var rulesPath = Utils.RelativeToFullPath(rulesFilePath);
            Rule? removed = null;
            List<string>? domainIpsToRemove = null;

            rwl.AcquireWriterLock(2000);
            try
            {
                var match = _rules.FirstOrDefault(r => r.Value.Id == id);
                var key = match.Key;
                if (key != null && _rules.Remove(key))
                {
                    removed = match.Value;
                    if (removed.Kind == RuleKind.Domain &&
                        _domainResolvedIps.TryGetValue(removed.Path, out var ips))
                    {
                        domainIpsToRemove = [.. ips];
                        _domainResolvedIps.Remove(removed.Path);
                    }
                    RewriteRulesLocked(rulesPath);
                }
            }
            finally
            {
                rwl.ReleaseWriterLock();
            }

            // Lift any active firewall block for the removed rule (firewall product only).
            if (removed != null)
            {
                if (removed.Type == RuleType.Block && AppInfo.IsFirewall)
                {
                    try
                    {
                        if (removed.Kind == RuleKind.Domain)
                        {
                            if (domainIpsToRemove != null)
                            {
                                foreach (var ip in domainIpsToRemove)
                                    Firewall.DeleteRuleByName(Firewall.DomainIpRuleNameFor(removed.Path, ip));
                            }
                        }
                        else
                        {
                            Firewall.UnblockRule(removed);
                        }
                    }
                    catch (Exception ex) { Logger.LogException(ex); }
                }

                RulesChanged?.Invoke(removed);
            }
        }

        /// <summary>
        /// Checks whether <paramref name="domain"/> matches any stored <see cref="RuleKind.Domain"/>
        /// block rule (exact match or subdomain of a blocked domain) and, if so, registers and
        /// enforces a firewall block on <paramref name="ip"/>.
        /// </summary>
        public bool EnforceDomainResolution(string domain, string ip)
        {
            if (string.IsNullOrWhiteSpace(domain) || !Firewall.IsValidIpOrCidr(ip))
                return false;

            var normalizedDomain = domain.Trim().TrimEnd('.').ToLowerInvariant();
            string? matchedRuleDomain = null;
            string? matchedProfile = null;
            bool isNewIp = false;

            rwl.AcquireWriterLock(2000);
            try
            {
                foreach (var rule in _rules.Values)
                {
                    if (rule.Kind != RuleKind.Domain || rule.Type != RuleType.Block)
                        continue;

                    var target = rule.Path.Trim().TrimEnd('.').ToLowerInvariant();
                    if (normalizedDomain.Equals(target, StringComparison.OrdinalIgnoreCase) ||
                        normalizedDomain.EndsWith("." + target, StringComparison.OrdinalIgnoreCase))
                    {
                        matchedRuleDomain = rule.Path;
                        matchedProfile = rule.Profile;
                        if (!_domainResolvedIps.TryGetValue(rule.Path, out var ips))
                        {
                            ips = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            _domainResolvedIps[rule.Path] = ips;
                        }
                        isNewIp = ips.Add(ip.Trim());
                        break;
                    }
                }
            }
            finally
            {
                rwl.ReleaseWriterLock();
            }

            if (matchedRuleDomain == null)
                return false;

            if (isNewIp && AppInfo.IsFirewall)
            {
                var effProfile = Firewall.ResolveProfile(matchedProfile, ActiveProfile);
                try { Firewall.BlockDomainIp(matchedRuleDomain, ip.Trim(), effProfile); }
                catch (Exception ex) { Logger.LogException(ex); }
            }

            return true;
        }

        /// <summary>
        /// Re-applies every block rule to the OS firewall and reconciles stale netsh
        /// state (firewall product only). Drops <c>Xvirus_Block_*</c> netsh rules that no
        /// longer correspond to a stored block rule, and re-adds stored block rules whose
        /// netsh enforcement is missing (or re-adds all rules when <paramref name="forceReapply"/>
        /// is true, e.g., after a firewall profile change).
        /// </summary>
        public void SyncEnforcement(string? activeProfile = null, bool forceReapply = false)
        {
            if (activeProfile != null)
                ActiveProfile = Firewall.NormalizeProfile(activeProfile);

            if (!AppInfo.IsFirewall) return;

            List<Rule> blockRules;
            List<(string domain, string ip, string profile)> domainIpBlocks = new();

            rwl.AcquireReaderLock(2000);
            try
            {
                blockRules = _rules.Values.Where(r => r.Type == RuleType.Block).ToList();
                foreach (var rule in blockRules)
                {
                    if (rule.Kind == RuleKind.Domain &&
                        _domainResolvedIps.TryGetValue(rule.Path, out var ips))
                    {
                        foreach (var ip in ips)
                            domainIpBlocks.Add((rule.Path, ip, rule.Profile));
                    }
                }
            }
            finally
            {
                rwl.ReleaseReaderLock();
            }

            // Expected rule names for every stored block rule (program, ip, port) and resolved domain IPs.
            var expectedRules = new Dictionary<string, Rule>(StringComparer.Ordinal);
            foreach (var rule in blockRules)
            {
                if (rule.Kind == RuleKind.Domain || string.IsNullOrWhiteSpace(rule.Path))
                    continue;
                expectedRules[Firewall.RuleNameFor(rule)] = rule;
            }

            var expectedDomainIps = new Dictionary<string, (string domain, string ip, string profile)>(StringComparer.Ordinal);
            foreach (var item in domainIpBlocks)
            {
                expectedDomainIps[Firewall.DomainIpRuleNameFor(item.domain, item.ip)] = item;
            }

            // Existing Xvirus_Block_* rules in Windows Firewall.
            List<string> existing;
            try { existing = Firewall.ListBlockedRules(); }
            catch (Exception ex)
            {
                Logger.LogException(ex);
                existing = new List<string>();
            }

            var existingSet = new HashSet<string>(existing, StringComparer.Ordinal);

            // Drop orphans: netsh rules with no matching stored block rule.
            foreach (var name in existing)
            {
                if (expectedRules.ContainsKey(name) || expectedDomainIps.ContainsKey(name)) continue;
                try
                {
                    if (Firewall.DeleteRuleByName(name))
                        Logger.LogHistory("firewall", $"Reconciled orphan firewall rule '{name}' (removed).");
                }
                catch (Exception ex) { Logger.LogException(ex); }
            }

            // Re-add missing (or all when profile changed): stored block rules.
            foreach (var (name, rule) in expectedRules)
            {
                if (!forceReapply && existingSet.Contains(name)) continue;
                try
                {
                    Firewall.BlockRule(rule, ActiveProfile);
                }
                catch (Exception ex) { Logger.LogException(ex); }
            }

            foreach (var (name, item) in expectedDomainIps)
            {
                if (!forceReapply && existingSet.Contains(name)) continue;
                try
                {
                    Firewall.BlockDomainIp(item.domain, item.ip, Firewall.ResolveProfile(item.profile, ActiveProfile));
                }
                catch (Exception ex) { Logger.LogException(ex); }
            }
        }

        // Applies (or lifts) OS-level firewall enforcement for a single rule. No-op for
        // the anti-malware product, where rules stay advisory hints for the scanner.
        private void ApplyEnforcement(Rule rule)
        {
            if (!AppInfo.IsFirewall) return;
            try
            {
                if (rule.Type == RuleType.Block)
                    Firewall.BlockRule(rule, ActiveProfile);
                else if (rule.Kind != RuleKind.Domain)
                    Firewall.UnblockRule(rule);
            }
            catch (Exception ex)
            {
                Logger.LogException(ex);
            }
        }

        private void RewriteRulesLocked(string rulesPath)
        {
            if (_rules.Count == 0)
            {
                File.WriteAllText(rulesPath, string.Empty);
                return;
            }
            var allRulesJson = string.Join(
                Environment.NewLine,
                _rules.Values.Select(r => JsonSerializer.Serialize(r, SourceGenerationContext.Default.Rule)));
            File.WriteAllText(rulesPath, allRulesJson + Environment.NewLine);
        }

        public RuleType? GetRuleType(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            rwl.AcquireReaderLock(2000);
            try
            {
                return _rules.GetValueOrDefault(BuildKey(RuleKind.Program, path))?.Type;
            }
            finally
            {
                rwl.ReleaseReaderLock();
            }
        }

        public List<Rule> GetAllRules()
        {
            rwl.AcquireReaderLock(2000);
            try
            {
                return [.. _rules.Values];
            }
            finally
            {
                rwl.ReleaseReaderLock();
            }
        }

        private static Dictionary<string, Rule> GetRules(string rulesFilePath = "rules.json", bool seedRunningProcesses = true)
        {
            var result = new Dictionary<string, Rule>(StringComparer.OrdinalIgnoreCase);
            var path = Utils.RelativeToFullPath(rulesFilePath);
            try
            {
                if (File.Exists(path))
                {
                    foreach (var line in File.ReadAllLines(path))
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        try
                        {
                            var e = JsonSerializer.Deserialize(line, SourceGenerationContext.Default.Rule);
                            if (e != null && !string.IsNullOrWhiteSpace(e.Path))
                                result[BuildKey(e)] = e;
                        }
                        catch { /* ignore parse errors */ }
                    }
                }
                else if (seedRunningProcesses)
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

                            var rule = new Rule { Kind = RuleKind.Program, Path = exePath, Type = RuleType.Allow };
                            lines.Add(JsonSerializer.Serialize(rule, SourceGenerationContext.Default.Rule));
                            result[BuildKey(rule)] = rule;
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
