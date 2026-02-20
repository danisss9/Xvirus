using XvirusService;
using Xvirus;
using Xvirus.Model;
using XvirusService.Api;

// Create the web application builder
var builder = WebApplication.CreateSlimBuilder(args);

// Configure for Windows Service
builder.Host.UseWindowsService(o =>
{
    o.ServiceName = "XvirusService";
});

// Register services
builder.Services.AddSingleton<RealTimeScanner>();
builder.Services.AddSingleton<ServerEventService>();
builder.Services.AddSingleton<ScanHistory>();

// Configure JSON serialization for Native AOT
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
});

var app = builder.Build();

// Register API endpoints
app.MapSettingsEndpoints();


// Scan API endpoint
app.MapPost("/scan", (ScanRequest request, ScanHistory history) =>
{
    try
    {
        Xvirus.XvirusSDK.GetSettings();
        var settings = Settings.Load();
        var database = new DB(settings);
        var ai = new AI(settings);
        var scanner = new Scanner(settings, database, ai);

        var results = new List<ScanResult>();
        int threatCount = 0;
        int fileCount = 0;

        foreach (var result in scanner.ScanFolder(request.Path))
        {
            results.Add(result);
            fileCount++;
            if (result.IsMalware)
                threatCount++;
        }

        // Add to history
        history.AddEntry(new ScanHistoryEntry(
            Type: "Scan",
            Timestamp: DateTime.Now,
            Details: request.Path,
            FilesScanned: fileCount,
            ThreatsFound: threatCount
        ));

        return Results.Ok(new { FilesScanned = fileCount, ThreatsFound = threatCount, Results = results });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new ErrorResponseDTO { Error = ex.Message });
    }
});

// History API endpoint
app.MapGet("/history", (ScanHistory history) =>
{
    return Results.Ok(history.GetEntries());
});

// Server-Sent Events endpoint
app.MapGet("/events", (ServerEventService eventService, HttpContext context, CancellationToken cancellationToken)
    => eventService.HandleEvents(context, cancellationToken));

// Start the Real-Time Scanner
var scanner = app.Services.GetRequiredService<RealTimeScanner>();
scanner.Start();

// Graceful shutdown
var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
lifetime.ApplicationStopping.Register(() =>
{
    scanner.Stop();
    scanner.Dispose();
});

app.Run();
