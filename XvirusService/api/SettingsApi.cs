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

            app.MapPut("/settings", (SettingsResponseDTO newSettings) =>
            {
                try
                {
                    Settings.Save(newSettings.Settings);
                    Settings.SaveAppSettings(newSettings.AppSettings);
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
