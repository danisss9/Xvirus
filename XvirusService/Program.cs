using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Principal;
using Xvirus;
using XvirusService;
using XvirusService.Api;
using XvirusService.Services;

[assembly: SupportedOSPlatform("windows")]

// Require administrator privileges
if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
{
    Console.Error.WriteLine("XvirusService requires administrator privileges.");
    return;
}

// Create the web application builder
var builder = WebApplication.CreateSlimBuilder(args);

// Configure for Windows Service
builder.Host.UseWindowsService(o =>
{
    o.ServiceName = "XvirusService";
});

// Register services
builder.Services.AddSingleton<SettingsService>();
builder.Services.AddSingleton<WindowsStartupService>();
builder.Services.AddSingleton<ServerEventService>();
builder.Services.AddSingleton<Rules>();
builder.Services.AddSingleton<Quarantine>();
builder.Services.AddSingleton<ThreatAlertService>();
builder.Services.AddSingleton<NetworkService>();
builder.Services.AddSingleton<RealTimeProtection>();
builder.Services.AddSingleton<NetworkRealTimeProtection>();
builder.Services.AddHostedService<AutoUpdater>();

// Scanner and its dependencies
builder.Services.AddSingleton(sp =>
    new DB(sp.GetRequiredService<SettingsService>().Settings));
builder.Services.AddSingleton(sp =>
    new AI(sp.GetRequiredService<SettingsService>().Settings));
builder.Services.AddSingleton(sp =>
    new Scanner(
        sp.GetRequiredService<SettingsService>().Settings,
        sp.GetRequiredService<DB>(),
        sp.GetRequiredService<AI>(),
        sp.GetRequiredService<Rules>()));

// Configure JSON serialization for Native AOT
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
});

var app = builder.Build();

// Register API endpoints
app.MapSettingsEndpoints();
app.MapHistoryEndpoints();
app.MapRulesEndpoints();
app.MapQuarantineEndpoints();
app.MapUpdateEndpoints();
app.MapNetworkEndpoints();
app.MapActionsEndpoints();
app.MapServerSentEvents();

// Start protection services
var protection = app.Services.GetRequiredService<RealTimeProtection>();
var networkProtection = app.Services.GetRequiredService<NetworkRealTimeProtection>();
protection.Start();
networkProtection.Start();

// Graceful shutdown
var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
lifetime.ApplicationStopping.Register(() =>
{
    protection.Stop();
    protection.Dispose();
    networkProtection.Stop();
    networkProtection.Dispose();
});

// Launch the UI if it is not already running
var uiExe = Path.Combine(AppContext.BaseDirectory, "XvirusUI.exe");
if (File.Exists(uiExe) && Process.GetProcessesByName("XvirusUI").Length == 0)
{
    Process.Start(new ProcessStartInfo
    {
        FileName = uiExe,
        UseShellExecute = false,
        CreateNoWindow = true,
        WindowStyle = ProcessWindowStyle.Hidden,
    });
}

app.Run();
