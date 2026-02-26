using System;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Threading.Tasks;
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

// allow any cross‑origin request (frontend may be served separately)
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader());
});

// listen on fixed port for service clients
builder.WebHost.UseUrls("http://*:5236");

var app = builder.Build();

// Register API endpoints
app.UseCors("AllowAll");
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

// Launch the UI shortly after startup (delay to let service settle).
// Only attempt this when we're running in an interactive session.  if
// the service is started by the SCM it runs in session 0 as NT AUTHORITY\SYSTEM
// and cannot create a user‑visible process; starting it there leads to the
// "cannot create data directory" error seen in the screenshot.
var uiExe = Path.Combine(AppContext.BaseDirectory, "XvirusUI.exe");
if (Process.GetCurrentProcess().SessionId != 0)
{
    _ = Task.Run(async () =>
    {
        await Task.Delay(TimeSpan.FromSeconds(10));
        try
        {
            Console.WriteLine($"Checking for UI at: {uiExe} and processes exists? {File.Exists(uiExe)}");
            if (File.Exists(uiExe))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = uiExe,
                    UseShellExecute = true,
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to launch UI: {ex.Message}");
        }
    });
}
else
{
    Console.WriteLine("Skipping UI launch: running in service session.");
}

app.Run();
