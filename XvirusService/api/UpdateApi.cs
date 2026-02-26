using Xvirus;
using Xvirus.Model;
using XvirusService.Model;
using XvirusService.Services;

namespace XvirusService.Api
{
    public static class UpdateApi
    {
        public static void MapUpdateEndpoints(this WebApplication app)
        {
            app.MapGet("/update/lastcheck", (SettingsService settingsService) =>
            {
                try
                {
                    var dto = new UpdateStatusDTO { LastUpdateCheck = settingsService.Settings.LastUpdateCheck };
                    return Results.Ok(dto);
                }
                catch (Exception ex)
                {
                    return Results.BadRequest(new ErrorResponseDTO { Error = ex.Message });
                }
            });

            app.MapPost("/update/check", (SettingsService settingsService, DB db, AI ai) =>
            {
                try
                {
                    var message = Updater.CheckUpdates(settingsService.Settings);
                    settingsService.Reload();
                    db.Load(settingsService.Settings);
                    ai.Load(settingsService.Settings);

                    var dto = new UpdateStatusDTO
                    {
                        Message = message,
                        LastUpdateCheck = settingsService.Settings.LastUpdateCheck,
                    };
                    return Results.Ok(dto);
                }
                catch (Exception ex)
                {
                    return Results.BadRequest(new ErrorResponseDTO { Error = ex.Message });
                }
            });
        }
    }
}
