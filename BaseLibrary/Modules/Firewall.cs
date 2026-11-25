using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Xvirus.Model;

namespace Xvirus
{
    /// <summary>
    /// Windows Firewall enforcement via <c>netsh advfirewall</c>. Used by the
    /// Firewall product to turn stored block rules (program, IP/CIDR, port, and
    /// dynamically resolved domain IPs) into real inbound/outbound blocks without
    /// a kernel driver. Each rule is named with a stable
    /// <c>Xvirus_Block_&lt;hash&gt;</c> prefix so it can be reconciled and removed
    /// deterministically.
    /// </summary>
    public static class Firewall
    {
        public const string RulePrefix = "Xvirus_Block_";

        public static bool IsSupported => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        /// <summary>Stable, collision-resistant rule name derived from a raw block target.</summary>
        public static string RuleNameFor(string target)
        {
            var hash = SHA1.HashData(Encoding.Unicode.GetBytes(target.Trim().ToLowerInvariant()));
            return RulePrefix + Convert.ToHexString(hash, 0, 8); // 16 hex chars
        }

        /// <summary>
        /// Stable rule name for a <see cref="Rule"/>. Preserves legacy naming for
        /// standard program rules (both directions, any protocol) while disambiguating
        /// IP, port, domain, and protocol/direction-scoped rules.
        /// </summary>
        public static string RuleNameFor(Rule rule)
        {
            var proto = NormalizeProtocol(rule.Protocol);
            var dir = NormalizeDirection(rule.Direction);
            if (rule.Kind == RuleKind.Program && proto == "any" && dir == "both")
                return RuleNameFor(rule.Path);

            var key = $"{rule.Kind.ToString().ToLowerInvariant()}|{rule.Path.Trim().ToLowerInvariant()}|{proto}|{dir}";
            return RuleNameFor(key);
        }

        /// <summary>Stable rule name for a dynamically resolved IP belonging to a blocked domain.</summary>
        public static string DomainIpRuleNameFor(string domain, string ip)
        {
            var key = $"domain-ip|{domain.Trim().ToLowerInvariant()}|{ip.Trim().ToLowerInvariant()}";
            return RuleNameFor(key);
        }

        public static string NormalizeDirection(string? direction) => (direction ?? "both").Trim().ToLowerInvariant() switch
        {
            "in" => "in",
            "out" => "out",
            _ => "both"
        };

        public static string NormalizeProtocol(string? protocol) => (protocol ?? "any").Trim().ToLowerInvariant() switch
        {
            "tcp" => "tcp",
            "udp" => "udp",
            _ => "any"
        };

        public static string NormalizeProfile(string? profile) => (profile ?? "any").Trim().ToLowerInvariant() switch
        {
            "private" or "home" => "private",
            "domain" or "work" => "domain",
            "public" => "public",
            _ => "any"
        };

        /// <summary>
        /// Resolves the effective Windows Firewall profile for a rule: a specific
        /// rule-level profile takes precedence over the global active profile.
        /// </summary>
        public static string ResolveProfile(string? ruleProfile, string? activeProfile)
        {
            var rp = NormalizeProfile(ruleProfile);
            return rp != "any" ? rp : NormalizeProfile(activeProfile);
        }

        /// <summary>Blocks all inbound and outbound traffic for a program by full path.</summary>
        public static bool BlockProgram(string exePath, string direction = "both", string protocol = "any", string profile = "any")
        {
            if (!IsSupported || string.IsNullOrWhiteSpace(exePath))
                return false;

            var rule = new Rule
            {
                Kind = RuleKind.Program,
                Path = exePath,
                Type = RuleType.Block,
                Direction = direction,
                Protocol = protocol,
                Profile = profile
            };
            return BlockRule(rule, profile);
        }

        /// <summary>Removes the block rule previously created for a program path.</summary>
        public static bool UnblockProgram(string exePath)
        {
            if (!IsSupported || string.IsNullOrWhiteSpace(exePath))
                return false;

            var name = RuleNameFor(exePath);
            bool removed = DeleteRuleByName(name);
            if (removed)
                Logger.LogHistory("firewall", $"Unblocked program '{exePath}' (rule {name}).");
            return removed;
        }

        /// <summary>Blocks inbound/outbound traffic to/from an IP address or CIDR subnet.</summary>
        public static bool BlockIp(string ipOrCidr, string direction = "both", string protocol = "any", string profile = "any")
        {
            if (!IsSupported || !IsValidIpOrCidr(ipOrCidr))
                return false;

            var rule = new Rule
            {
                Kind = RuleKind.Ip,
                Path = ipOrCidr.Trim(),
                Type = RuleType.Block,
                Direction = direction,
                Protocol = protocol,
                Profile = profile
            };
            return BlockRule(rule, profile);
        }

        /// <summary>Blocks inbound/outbound traffic for a TCP or UDP port (or port range like "8000-8080").</summary>
        public static bool BlockPort(string portSpec, string protocol = "tcp", string direction = "both", string profile = "any")
        {
            if (!IsSupported || !IsValidPortSpec(portSpec))
                return false;

            var rule = new Rule
            {
                Kind = RuleKind.Port,
                Path = portSpec.Trim(),
                Type = RuleType.Block,
                Direction = direction,
                Protocol = protocol,
                Profile = profile
            };
            return BlockRule(rule, profile);
        }

        /// <summary>Blocks a dynamically resolved IP address associated with a blocked domain.</summary>
        public static bool BlockDomainIp(string domain, string ip, string profile = "any")
        {
            if (!IsSupported || string.IsNullOrWhiteSpace(domain) || !IsValidIpOrCidr(ip))
                return false;

            var name = DomainIpRuleNameFor(domain, ip);
            var effProfile = NormalizeProfile(profile);
            DeleteRuleByName(name);

            bool outOk = RunNetsh(BuildAddRuleArgs(name, RuleKind.Ip, ip.Trim(), "out", "any", effProfile));
            bool inOk = RunNetsh(BuildAddRuleArgs(name, RuleKind.Ip, ip.Trim(), "in", "any", effProfile));

            if (outOk || inOk)
                Logger.LogHistory("firewall", $"Blocked domain '{domain}' resolved IP '{ip}' (rule {name}, profile {effProfile}).");
            return outOk && inOk;
        }

        /// <summary>Applies OS-level netsh enforcement for a Program, IP, or Port block rule.</summary>
        public static bool BlockRule(Rule rule, string activeProfile = "any")
        {
            if (!IsSupported || string.IsNullOrWhiteSpace(rule.Path))
                return false;

            // Domain rules are enforced via BlockDomainIp as IPs resolve (plus DNS cache).
            if (rule.Kind == RuleKind.Domain)
                return true;

            if (rule.Kind == RuleKind.Ip && !IsValidIpOrCidr(rule.Path))
                return false;
            if (rule.Kind == RuleKind.Port && !IsValidPortSpec(rule.Path))
                return false;

            var name = RuleNameFor(rule);
            var dir = NormalizeDirection(rule.Direction);
            var proto = NormalizeProtocol(rule.Protocol);
            var profile = ResolveProfile(rule.Profile, activeProfile);

            // Replace any existing rule with the same name first (idempotent).
            DeleteRuleByName(name);

            var directions = dir == "both" ? new[] { "out", "in" } : new[] { dir };
            // Windows Firewall requires a specific protocol (TCP or UDP) when filtering by port.
            var protocols = (rule.Kind == RuleKind.Port && proto == "any")
                ? new[] { "tcp", "udp" }
                : new[] { proto };

            bool allOk = true;
            bool anyOk = false;

            foreach (var d in directions)
            {
                foreach (var p in protocols)
                {
                    var args = BuildAddRuleArgs(name, rule.Kind, rule.Path.Trim(), d, p, profile);
                    bool ok = RunNetsh(args);
                    allOk &= ok;
                    anyOk |= ok;
                }
            }

            if (anyOk)
                Logger.LogHistory("firewall", $"Blocked {rule.Kind.ToString().ToLowerInvariant()} '{rule.Path}' (rule {name}, dir={dir}, proto={proto}, profile={profile}).");

            return allOk;
        }

        /// <summary>Removes any OS-level block rule associated with <paramref name="rule"/>.</summary>
        public static bool UnblockRule(Rule rule)
        {
            if (!IsSupported || string.IsNullOrWhiteSpace(rule.Path))
                return false;

            var name = RuleNameFor(rule);
            bool removed = DeleteRuleByName(name);
            if (removed)
                Logger.LogHistory("firewall", $"Unblocked {rule.Kind.ToString().ToLowerInvariant()} '{rule.Path}' (rule {name}).");
            return removed;
        }

        /// <summary>
        /// Builds the exact <c>netsh</c> argument vector for adding a single directional block rule.
        /// Exposed internally for unit testing without mutating the host firewall.
        /// </summary>
        public static string[] BuildAddRuleArgs(string name, RuleKind kind, string target, string dir, string proto, string profile)
        {
            var args = new List<string>
            {
                "advfirewall", "firewall", "add", "rule",
                $"name={name}",
                $"dir={dir}",
                "action=block",
                "enable=yes",
                $"profile={NormalizeProfile(profile)}"
            };

            switch (kind)
            {
                case RuleKind.Program:
                    args.Add($"program={target}");
                    if (proto != "any") args.Add($"protocol={proto}");
                    break;

                case RuleKind.Ip:
                    args.Add($"remoteip={target}");
                    if (proto != "any") args.Add($"protocol={proto}");
                    break;

                case RuleKind.Port:
                    var portProto = proto == "udp" ? "udp" : "tcp";
                    args.Add($"protocol={portProto}");
                    args.Add(dir == "in" ? $"localport={target}" : $"remoteport={target}");
                    break;
            }

            return args.ToArray();
        }

        public static bool IsValidIpOrCidr(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            var trimmed = value.Trim();
            var slash = trimmed.IndexOf('/');
            if (slash < 0)
                return IPAddress.TryParse(trimmed, out _);

            var ipPart = trimmed.Substring(0, slash);
            var prefixPart = trimmed.Substring(slash + 1);
            if (!IPAddress.TryParse(ipPart, out var ip)) return false;
            if (!int.TryParse(prefixPart, out var prefix)) return false;
            int maxBits = ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? 128 : 32;
            return prefix >= 0 && prefix <= maxBits;
        }

        public static bool IsValidPortSpec(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            var trimmed = value.Trim();
            var dash = trimmed.IndexOf('-');
            if (dash < 0)
                return int.TryParse(trimmed, out var p) && p is >= 1 and <= 65535;

            var startStr = trimmed.Substring(0, dash);
            var endStr = trimmed.Substring(dash + 1);
            return int.TryParse(startStr, out var s)
                && int.TryParse(endStr, out var e)
                && s >= 1 && e <= 65535 && s <= e;
        }

        /// <summary>
        /// Enumerates every <c>Xvirus_Block_*</c> firewall rule currently registered in
        /// Windows Firewall (both inbound and outbound). Names are deduplicated — a single
        /// block can produce multiple netsh rules sharing one name.
        /// </summary>
        public static List<string> ListBlockedRules()
        {
            var result = new List<string>();
            if (!IsSupported) return result;

            string? output = RunNetshCapture("advfirewall", "firewall", "show", "rule", "name=all");
            if (string.IsNullOrEmpty(output)) return result;

            // We control the rule names (Xvirus_Block_<16 hex>), so scan the whole output
            // for that pattern rather than relying on localized "Rule Name:" labels.
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match m in BlockRuleNameRegex.Matches(output))
            {
                if (seen.Add(m.Value))
                    result.Add(m.Value);
            }
            return result;
        }

        // -------------------------------------------------------------------

        private static readonly Regex BlockRuleNameRegex =
            new(RulePrefix + "[0-9A-F]{16}", RegexOptions.Compiled);

        /// <summary>Deletes every netsh rule with the given name (all directions/protocols).</summary>
        public static bool DeleteRuleByName(string name)
        {
            // Returns false when no rule existed; netsh exits non-zero ("No rules match").
            return RunNetsh("advfirewall", "firewall", "delete", "rule", $"name={name}");
        }

        private static bool RunNetsh(params string[] args)
        {
            try
            {
                var psi = BuildNetshStartInfo(args);
                using var proc = Process.Start(psi);
                if (proc == null) return false;

                var errTask = proc.StandardError.ReadToEndAsync();
                proc.StandardOutput.ReadToEnd();
                errTask.GetAwaiter().GetResult();
                proc.WaitForExit(10000);
                return proc.HasExited && proc.ExitCode == 0;
            }
            catch (Exception ex)
            {
                Logger.LogException(ex);
                return false;
            }
        }

        /// <summary>Runs netsh and captures stdout (used for rule enumeration).</summary>
        private static string? RunNetshCapture(params string[] args)
        {
            try
            {
                var psi = BuildNetshStartInfo(args);
                using var proc = Process.Start(psi);
                if (proc == null) return null;

                var errTask = proc.StandardError.ReadToEndAsync();
                string stdout = proc.StandardOutput.ReadToEnd();
                errTask.GetAwaiter().GetResult();
                proc.WaitForExit(30000);
                return proc.HasExited ? stdout : null;
            }
            catch (Exception ex)
            {
                Logger.LogException(ex);
                return null;
            }
        }

        private static ProcessStartInfo BuildNetshStartInfo(string[] args)
        {
            var psi = new ProcessStartInfo("netsh")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            return psi;
        }
    }
}
