using System.Diagnostics;
using System.Runtime.Versioning;

namespace XvirusService.Services;

/// <summary>
/// Lightweight tamper resistance for the Firewall / Anti-Malware service. When enabled,
/// configures Windows Service recovery so the protection service automatically restarts
/// if it is killed. (No kernel driver — this is a deterrent, not full tamper protection.)
/// </summary>
[SupportedOSPlatform("windows")]
public class SelfDefenseService
{
    private static string ServiceName => Xvirus.AppInfo.ServiceName;

    public void Apply(bool enabled)
    {
        try
        {
            // reset= window (seconds) after which the failure count resets; actions list
            // restarts the service three times, 60s apart. Disabling clears the actions.
            var args = enabled
                ? $"failure {ServiceName} reset= 86400 actions= restart/60000/restart/60000/restart/60000"
                : $"failure {ServiceName} reset= 0 actions= \"\"";
            RunSc(args);
        }
        catch
        {
            // Not running as a registered service (dev mode) — non-fatal.
        }
    }

    private static void RunSc(string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "sc.exe",
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var process = Process.Start(psi);
        process?.WaitForExit(5000);
    }
}
