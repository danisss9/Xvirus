using XvirusService.Services;

namespace XvirusService.Api;

public static class NetworkApi
{
    public static void MapNetworkEndpoints(this WebApplication app)
    {
        app.MapGet("/network/connections", async (NetworkService networkService) =>
        {
            var connections = await networkService.GetConnectionsAsync();
            return Results.Ok(connections);
        });
    }
}
