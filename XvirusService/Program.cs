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

// Which product this service belongs to (anti-malware vs firewall) is baked in at build
// time via the FIREWALL compile constant (/p:ProductMode=firewall). Drives the update
// endpoint (AppInfo.AppCode) and firewall-only enforcement.
#if FIREWALL
AppInfo.AppCode = "firewall";
#else
AppInfo.AppCode = "antimalware";
#endif
Console.WriteLine($"XvirusService: running as '{AppInfo.AppCode}' (v{AppInfo.GetVersion()}).");

// Create the web application builder
var builder = WebApplication.CreateSlimBuilder(args);

// Configure for Windows Service
builder.Host.UseWindowsService(o =>
{
    o.ServiceName = AppInfo.ServiceName;
});

// Register services
builder.Services.AddSingleton<SettingsService>();
builder.Services.AddSingleton<WindowsStartupService>();
builder.Services.AddSingleton<ContextMenuService>();
builder.Services.AddSingleton<SelfDefenseService>();
builder.Services.AddSingleton<ServerEventService>();
builder.Services.AddSingleton<Rules>();
builder.Services.AddSingleton<Quarantine>();
builder.Services.AddSingleton<ThreatAlertService>();
builder.Services.AddSingleton<NetworkService>();
builder.Services.AddSingleton<RealTimeProtection>();
builder.Services.AddSingleton<NetworkRealTimeProtection>();
builder.Services.AddSingleton<ScanService>();
builder.Services.AddHostedService<AutoUpdater>();
builder.Services.AddHostedService<ScheduledScanService>();

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

// Listen on loopback only — the UI talks to http://localhost:5236 and the API must not
// be reachable from the LAN.
builder.WebHost.UseUrls("http://localhost:5236");

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
app.MapScanEndpoints();
app.MapServerSentEvents();

// Firewall product: re-apply persisted block rules to the OS firewall (no-op for AM).
app.Services.GetRequiredService<Rules>().SyncEnforcement();

// Reconcile the Explorer "Scan with Xvirus" context-menu entry with current settings.
var bootSettings = app.Services.GetRequiredService<SettingsService>();
app.Services.GetRequiredService<ContextMenuService>().Apply(bootSettings.AppSettings.EnableContextMenu);

// Reconcile self-defense (service auto-restart) with current settings.
app.Services.GetRequiredService<SelfDefenseService>().Apply(bootSettings.AppSettings.SelfDefense);

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

app.Run();
