using XvirusService;
using XvirusService.Api;
using XvirusService.Services;

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

// Configure JSON serialization for Native AOT
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
});

var app = builder.Build();

// Register API endpoints
app.MapSettingsEndpoints();
app.MapHistoryEndpoints();

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
