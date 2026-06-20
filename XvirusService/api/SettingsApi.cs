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

            app.MapPut("/settings", (SettingsResponseDTO newSettings, SettingsService settingsService, WindowsStartupService startupService, ContextMenuService contextMenuService, SelfDefenseService selfDefenseService) =>
            {
                try
                {
                    bool startWithWindowsChanged = settingsService.AppSettings.StartWithWindows != newSettings.AppSettings.StartWithWindows;
                    bool contextMenuChanged = settingsService.AppSettings.EnableContextMenu != newSettings.AppSettings.EnableContextMenu;
                    bool selfDefenseChanged = settingsService.AppSettings.SelfDefense != newSettings.AppSettings.SelfDefense;

                    Settings.Save(newSettings.Settings);
                    Settings.SaveAppSettings(newSettings.AppSettings);
                    settingsService.Reload();

                    if (startWithWindowsChanged)
                        startupService.Apply(newSettings.AppSettings.StartWithWindows);

                    if (contextMenuChanged)
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
