using Xvirus;
using XvirusService.Model;
using XvirusService.Services;

namespace XvirusService.Api
{
    public static class SettingsApi
    {
        public static void MapSettingsEndpoints(this WebApplication app)
        {
            app.MapGet("/settings", (SettingsService settingsService) =>
            {
                try
                {
                    var response = new SettingsResponseDTO { Settings = settingsService.Settings, AppSettings = settingsService.AppSettings };
                    return Results.Ok(response);
                }
                catch (Exception ex)
                {
                    return Results.BadRequest(new ErrorResponseDTO { Error = ex.Message });
                }
            });

            app.MapPut("/settings", (
                SettingsResponseDTO newSettings,
                SettingsService settingsService,
                Scanner scanner,
                Rules rules,
                NetworkService networkService,
                RealTimeProtection realTimeProtection,
                NetworkRealTimeProtection networkRealTimeProtection,
                WindowsStartupService startupService,
                ContextMenuService contextMenuService,
                SelfDefenseService selfDefenseService) =>
            {
                try
                {
                    bool startWithWindowsChanged = settingsService.AppSettings.StartWithWindows != newSettings.AppSettings.StartWithWindows;
                    bool contextMenuChanged = settingsService.AppSettings.EnableContextMenu != newSettings.AppSettings.EnableContextMenu;
                    bool selfDefenseChanged = settingsService.AppSettings.SelfDefense != newSettings.AppSettings.SelfDefense;
                    bool firewallProfileChanged = !string.Equals(
                        settingsService.AppSettings.FirewallProfile,
                        newSettings.AppSettings.FirewallProfile,
                        StringComparison.OrdinalIgnoreCase);

                    Settings.Save(newSettings.Settings);
                    Settings.SaveAppSettings(newSettings.AppSettings);
                    settingsService.Reload();

                    // Propagate updated settings to live singletons immediately.
                    scanner.UpdateSettings(settingsService.Settings);
                    networkService.InvalidateCache();
                    realTimeProtection.ApplySettings();
                    networkRealTimeProtection.ApplySettings();

                    if (firewallProfileChanged)
                        rules.SyncEnforcement(settingsService.AppSettings.FirewallProfile, forceReapply: true);

                    if (startWithWindowsChanged)
                        startupService.Apply(newSettings.AppSettings.StartWithWindows);

                    if (contextMenuChanged && !AppInfo.IsFirewall)
                        contextMenuService.Apply(newSettings.AppSettings.EnableContextMenu);

                    if (selfDefenseChanged)
                        selfDefenseService.Apply(newSettings.AppSettings.SelfDefense);

                    return Results.Ok();
                }
                catch (Exception ex)
                {
                    return Results.BadRequest(new ErrorResponseDTO { Error = ex.Message });
                }
            });
        }
    }
}
