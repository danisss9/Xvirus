
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
            { "sdk", "5.1.2.0" },
            { "cli", "5.1.2.0" }
        };

        public static string GetVersion()
        {
            return AppVersion[AppCode];
        }

        public static bool IsFirewall => AppCode == "firewall";
        public static bool IsAntimalware => AppCode == "antimalware";

        /// <summary>The Windows service name the installer registered for this product.</summary>
        public static string ServiceName => IsFirewall ? "XvirusFirewallService" : "XvirusAntiMalwareService";
    }
}