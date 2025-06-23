namespace Xvirus.Model
{
    public class SettingsDTO
    {
        // Engines to use in Scan
        public bool EnableSignatures { get; set; } = true;
        public bool EnableHeuristics { get; set; } = true;
        public bool EnableAIScan { get; set; } = true;

        // Scan Levels
        public int HeuristicsLevel { get; set; } = 4; // From 1 to 5, higher is more agressive
        public int AILevel { get; set; } = 10; // From 1 to 100, higher is more agressive

        // Scan Max Length
        public double? MaxScanLength { get; set; } = null;
        public double? MaxHeuristicsPeScanLength { get; set; } = 20971520; // 20MBs
        public double? MaxHeuristicsOthersScanLength { get; set; } = 10485760; // 10MBs
        public double? MaxAIScanLength { get; set; } = 20971520; // 20MBs

        // Update Settings
        public bool CheckSDKUpdates { get; set; } = true;
        public string DatabaseFolder { get; set; } = "Database";
        public DatabaseDTO DatabaseVersion { get; set; } = new DatabaseDTO();
    }
}
