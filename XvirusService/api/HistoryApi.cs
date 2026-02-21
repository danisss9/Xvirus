using Xvirus;

namespace XvirusService.Api
{
    public static class HistoryApi
    {
        public static void MapHistoryEndpoints(this WebApplication app)
        {
            // return persisted history from logger file
            app.MapGet("/history", () =>
            {
                var logs = Logger.GetHistoryLog();
                return Results.Ok(logs);
            });

            // clear persistent history
            app.MapDelete("/history", () =>
            {
                Logger.ClearHistoryLog();
                return Results.Ok();
            });
        }
    }
}
