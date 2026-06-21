using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Xvirus
{
    /// <summary>
    /// Minimal Windows Firewall enforcement via <c>netsh advfirewall</c>. Used by the
    /// Firewall product to turn stored block rules into real inbound/outbound blocks
    /// without a kernel driver. Each program block is named with a stable
    /// <c>Xvirus_Block_&lt;hash&gt;</c> prefix so it can be removed deterministically.
    /// </summary>
    public static class Firewall
    {
        private const string RulePrefix = "Xvirus_Block_";

        public static bool IsSupported => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        /// <summary>Stable, collision-resistant rule name derived from the block target.</summary>
        public static string RuleNameFor(string target)
        {
            var hash = SHA1.HashData(Encoding.Unicode.GetBytes(target.Trim().ToLowerInvariant()));
            return RulePrefix + Convert.ToHexString(hash, 0, 8); // 16 hex chars
        }

        /// <summary>Blocks all inbound and outbound traffic for a program by full path.</summary>
        public static bool BlockProgram(string exePath)
        {
            if (!IsSupported || string.IsNullOrWhiteSpace(exePath))
                return false;

            var name = RuleNameFor(exePath);
            // Replace any existing rule with the same name first (idempotent).
            DeleteRuleByName(name);

            bool outOk = RunNetsh("advfirewall", "firewall", "add", "rule",
                $"name={name}", "dir=out", "action=block", $"program={exePath}", "enable=yes");
            bool inOk = RunNetsh("advfirewall", "firewall", "add", "rule",
                $"name={name}", "dir=in", "action=block", $"program={exePath}", "enable=yes");

            if (outOk || inOk)
                Logger.LogHistory("firewall", $"Blocked program '{exePath}' (rule {name}).");
            return outOk && inOk;
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

        /// <summary>
        /// Enumerates every <c>Xvirus_Block_*</c> firewall rule currently registered in
        /// Windows Firewall (both inbound and outbound). Names are deduplicated — a single
        /// program block produces two netsh rules (in/out) sharing one name. Returns an
        /// empty list on non-Windows platforms or when no rules exist.
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

        /// <summary>Deletes every netsh rule with the given name (both directions).</summary>
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

                proc.StandardOutput.ReadToEnd();
                proc.StandardError.ReadToEnd();
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

                string stdout = proc.StandardOutput.ReadToEnd();
                proc.StandardError.ReadToEnd();
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
