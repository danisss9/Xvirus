using System;

namespace Xvirus.Model
{
    public class SettingsDTO
    {
        // Engines to use in Scan
        public bool EnableSignatures { get; set; } = true;
        public bool EnableHeuristics { get; set; } = true;
        public bool EnableAIScan { get; set; } = true;
        public bool EnableCloudScan { get; set; } = false;

        // Scan Levels
        public int HeuristicsLevel { get; set; } = 4; // From 1 to 5, higher is more agressive
        public int AILevel { get; set; } = 10; // From 1 to 100, higher is more agressive

        // Scan Max Length
        public double? MaxScanLength { get; set; } = null;
        public double? MaxHeuristicsPeScanLength { get; set; } = 20971520; // 20MBs
        public double? MaxHeuristicsOthersScanLength { get; set; } = 10485760; // 10MBs
        public double? MaxAIScanLength { get; set; } = 20971520; // 20MBs

        // Archive scanning
        public bool EnableArchiveScan { get; set; } = false;
        public int? MaxArchiveDepth { get; set; } = null; // default 3 levels of nested archives
        public double? MaxArchiveTotalSize { get; set; } = null; // default 100MB total extracted bytes
        public int? MaxArchiveFileCount { get; set; } = null; // default 1000 extracted entries

        // Update Settings
        public bool CheckSDKUpdates { get; set; } = true;
        public string DatabaseFolder { get; set; } = "Database";
        public DateTime? LastUpdateCheck { get; set; } = null;
        public DatabaseDTO DatabaseVersion { get; set; } = new DatabaseDTO();
    }
}
