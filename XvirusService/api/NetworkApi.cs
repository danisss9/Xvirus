using XvirusService.Model;
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

        // POST /network/block-pid/{pid} — resolve PID, add program block rule, drop active TCP sockets
        app.MapPost("/network/block-pid/{pid:int}", (int pid, NetworkBlockService blockService) =>
        {
            var result = blockService.BlockPid(pid);
            return result.Success
                ? Results.Ok(result)
                : Results.BadRequest(new ErrorResponseDTO { Error = result.Message });
        });

        // POST /network/block-ip — add remote IP/CIDR block rule and drop active TCP sockets
        app.MapPost("/network/block-ip", (BlockIpRequestDTO body, NetworkBlockService blockService) =>
        {
            var result = blockService.BlockIp(body);
            return result.Success
                ? Results.Ok(result)
                : Results.BadRequest(new ErrorResponseDTO { Error = result.Message });
        });

        // POST /network/kill-connection — drop an active IPv4 TCP connection via SetTcpEntry
        app.MapPost("/network/kill-connection", (KillConnectionRequestDTO body, NetworkBlockService blockService) =>
        {
            var result = blockService.KillConnection(body);
            return result.Success
                ? Results.Ok(result)
                : Results.BadRequest(new ErrorResponseDTO { Error = result.Message });
        });
    }
}
