namespace Xvirus.Model
{
    public class AppSettingsDTO
    {
        // UI Settings
        public string Language { get; set; } = "en"; // en, pt
        public bool DarkMode { get; set; } = true;

        // General Settings
        public bool StartWithWindows { get; set; } = true;
        public bool EnableContextMenu { get; set; } = false;
        public bool PasswordProtection { get; set; } = false;
        public bool EnableLogs { get; set; } = true;

        // Scan Extra Settings
        public bool OnlyScanExecutables { get; set; } = true;
        public bool AutoQuarantine { get; set; } = false;
        public string ScheduledScan { get; set; } = "off"; // off, daily, weekly, monthly

        // Protection features
        public bool RealTimeProtection { get; set; } = true;
        public string ThreatAction { get; set; } = "ask"; // auto, ask
        public bool BehaviorProtection { get; set; } = false;
        public bool CloudScan { get; set; } = false;
        public bool NetworkProtection { get; set; } = true;
        public bool SelfDefense { get; set; } = false;
        public bool ShowNotifications { get; set; } = true;
    }
}
