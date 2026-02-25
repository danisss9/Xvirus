using Xvirus.Model;

namespace XvirusService.Services;

public class SettingsService
{
    public required SettingsDTO Settings { get; set; }
    public required AppSettingsDTO AppSettings { get; set; }

    public SettingsService()
    {
        Reload();
    }

    public void Reload()
    {
        Settings = Xvirus.Settings.Load();
        AppSettings = Xvirus.Settings.LoadAppSettings();
    }

}
