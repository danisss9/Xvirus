using Xvirus.Model;

namespace XvirusService.Services;

public class SettingsService
{
    public required SettingsDTO Settings { get; set; }
    public required AppSettingsDTO AppSettings { get; set; }

    /// <summary>True when this instance is running as the Firewall product.</summary>
    public bool IsFirewall => Xvirus.AppInfo.IsFirewall;

    public SettingsService()
    {
        Reload();
    }

    public void Reload()
    {
        Settings = Xvirus.Settings.Load();
        AppSettings = Xvirus.Settings.LoadAppSettings();

        // Apply settings that drive shared engine behaviour on load and after every save.
        Xvirus.Logger.EnableLogging = AppSettings.EnableLogs;
    }

}
