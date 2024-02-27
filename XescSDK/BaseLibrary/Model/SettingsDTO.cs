namespace XescSDK.Model
{
    public class SettingsDTO
    {
        public bool EnableHeuristics { get; set; } = true;
        public bool EnableAIScan { get; set; } = false;
        public double? MaxScanLength { get; set; } = null;
        public string DatabaseFolder { get; set; } = "Database";
        public DatabaseDTO DatabaseVersion { get; set; } = new DatabaseDTO();
    }
}
