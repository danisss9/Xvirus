
using System.Collections.Generic;

namespace Xvirus
{
    public class AppInfo
    {
        // antimalware, firewall, sdk, cli
        public static string AppCode = "sdk";

        private static readonly Dictionary<string, string> AppVersion = new()
        {
            { "antimalware", "7.0.5.0" },
            { "firewall", "4.5.0.0" },
            { "sdk", "5.1.1.0" },
            { "cli", "5.1.1.0" }
        };

        public static string GetVersion()
        {
            return AppVersion[AppCode];
        }
    }
}