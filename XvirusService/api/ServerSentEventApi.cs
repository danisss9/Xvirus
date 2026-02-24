using XvirusService.Services;

namespace XvirusService.Api;

public static class ServerSentEventApi
{
    public static void MapServerSentEvents(this WebApplication app)
    {
        app.MapGet("/events", (ServerEventService eventService, HttpContext context, CancellationToken cancellationToken)
            => eventService.HandleEvents(context, cancellationToken));
    }
}
