namespace Xvirus.Model
{
    public class AppSettingsDTO
    {
        // UI Settings
        public string Language { get; set; } = "en"; // en, pt
        public bool DarkMode { get; set; } = true;

        // General Settings
        public bool StartWithWindows { get; set; } = true;
        public bool EnableContextMenu { get; set; } = true;
        public bool PasswordProtection { get; set; } = false;
        public bool EnableLogs { get; set; } = true;

        // Scan Extra Settings
        public bool OnlyScanExecutables { get; set; } = true;
        public bool AutoQuarantine { get; set; } = true;
        public string ScheduledScan { get; set; } = "off"; // off, daily, weekly, monthly

        // Protection features
        public bool RealTimeProtection { get; set; } = true;
        public string ThreatAction { get; set; } = "auto"; // auto, ask
        public bool BehaviorProtection { get; set; } = true;
        public bool CloudScan { get; set; } = false;
        public bool NetworkProtection { get; set; } = true;
        public bool SelfDefense { get; set; } = true;
        public bool ShowNotifications { get; set; } = true;
    }
}
