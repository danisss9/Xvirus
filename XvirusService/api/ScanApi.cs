using Xvirus;
using XvirusService.Model;
using XvirusService.Services;

namespace XvirusService.Api;

public static class ScanApi
{
    public static void MapScanEndpoints(this WebApplication app)
    {
        // POST /scan  { "path": "C:\\" }  — runs a folder scan, streams progress via SSE,
        // returns the final { filesScanned, threatsFound } summary.
        app.MapPost("/scan", async (ScanRequestDTO body, ScanService scan, HttpContext ctx) =>
        {
            var path = string.IsNullOrWhiteSpace(body?.Path) ? "C:\\" : body!.Path!;
            var result = await scan.ScanAsync(path, ctx.RequestAborted);
            return Results.Ok(result);
        });

        // POST /scan/cancel  { "path": "C:\\" }  — cancels the scan of that path,
        // or every running scan when no path is supplied.
        app.MapPost("/scan/cancel", (ScanRequestDTO body, Scanner scanner) =>
        {
            if (!string.IsNullOrWhiteSpace(body?.Path))
                scanner.CancelScan(body!.Path!);
            else
                scanner.CancelAllScans();
            return Results.Ok();
        });
    }
}
