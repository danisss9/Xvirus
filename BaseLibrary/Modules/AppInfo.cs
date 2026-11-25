
using System.Collections.Generic;

namespace Xvirus
{
    public class AppInfo
    {
        // antimalware, firewall, sdk, cli
        public static string AppCode = "sdk";

        private static readonly Dictionary<string, string> AppVersion = new()
        {
            { "antimalware", "8.0.0.0" },
            { "firewall", "5.0.0.0" },
            { "sdk", "5.2.2.0" },
            { "cli", "5.2.2.0" }
        };

        public static string GetVersion()
        {
            return AppVersion[AppCode];
        }

        public static bool IsFirewall => AppCode == "firewall";
        public static bool IsAntimalware => AppCode == "antimalware";

        /// <summary>Human-readable product name used for registry keys, shortcuts, and UI titles.</summary>
        public static string ProductName => IsFirewall ? "Xvirus Firewall" : "Xvirus Anti-Malware";

        /// <summary>The Windows service name the installer registered for this product.</summary>
        public static string ServiceName => IsFirewall ? "XvirusFirewallService" : "XvirusAntiMalwareService";

        /// <summary>Loopback HTTP port so Anti-Malware (5236) and Firewall (5237) can run side-by-side.</summary>
        public static int ServicePort => IsFirewall ? 5237 : 5236;

        /// <summary>Product-scoped ETW session prefix so Anti-Malware and Firewall sessions never collide.</summary>
        public static string EtwSessionPrefix => IsFirewall ? "XvirusFW_" : "XvirusAM_";
    }
}