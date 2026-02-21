using Xvirus;
using Xvirus.Model;
using XvirusService.Model;

namespace XvirusService.Api
{
    public static class SettingsApi
    {
        public static void MapSettingsEndpoints(this WebApplication app)
        {
            app.MapGet("/settings", () =>
            {
                try
                {
                    var settings = Settings.Load();
                    var appSettings = Settings.LoadAppSettings();
                    var response = new SettingsResponseDTO { Settings = settings, AppSettings = appSettings };
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
