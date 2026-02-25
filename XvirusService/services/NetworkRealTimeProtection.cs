using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Xvirus;

namespace XvirusService.Services;

[SupportedOSPlatform("windows")]
public class NetworkRealTimeProtection(
    SettingsService settings,
    Scanner scanner,
    Quarantine quarantine,
    ServerEventService events,
    ThreatAlertService alertService) : IDisposable
{
    private CancellationTokenSource? _cts;
    private Task? _monitorTask;

    // Paths already scanned this session – avoids re-scanning the same
    // executable every poll tick. Keyed on full path (case-insensitive).
    private readonly HashSet<string> _scannedPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _scannedLock = new();

    private bool _disposed;

    private static readonly Regex NetstatPidRe = new(
        @"^\s*(?:TCP|UDP)\s+\S+\s+\S+\s+(?:\S+\s+)?(\d+)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

    // -----------------------------------------------------------------------

    public void Start()
    {
        if (!settings.AppSettings.NetworkProtection)
        {
            Console.WriteLine("NetworkRealTimeProtection: disabled in settings, not starting.");
            return;
        }

        Console.WriteLine("NetworkRealTimeProtection: starting network connection monitor...");

        _cts = new CancellationTokenSource();
        _monitorTask = Task.Run(() => MonitorLoopAsync(_cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        Console.WriteLine("NetworkRealTimeProtection: stopped.");
    }

    // -----------------------------------------------------------------------
    // Poll loop
    // -----------------------------------------------------------------------

    private async Task MonitorLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));

        try
        {
            while (await timer.WaitForNextTickAsync(ct))
                await ScanNewConnectionsAsync(ct);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Console.WriteLine($"NetworkRealTimeProtection: monitor loop error – {ex.Message}");
        }
    }

    private async Task ScanNewConnectionsAsync(CancellationToken ct)
    {
        try
        {
            string output = await RunNetstatAsync(ct);

            foreach (int pid in ParsePids(output))
            {
                if (ct.IsCancellationRequested) return;

                string? path = ProcessControl.ResolveProcessPath(pid);
                if (string.IsNullOrEmpty(path)) continue;

                lock (_scannedLock)
                {
                    if (!_scannedPaths.Add(path)) continue;
                }

                // Fire-and-forget per process so we don't block the poll tick
                _ = HandleConnectionAsync(pid, path, ct);
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Console.WriteLine($"NetworkRealTimeProtection: poll error – {ex.Message}");
        }
    }

    // -----------------------------------------------------------------------
    // Per-process scan → threat response
    // -----------------------------------------------------------------------

    private async Task HandleConnectionAsync(int pid, string executablePath, CancellationToken ct)
    {
        try
        {
            var result = await Task.Run(() => scanner.ScanFile(executablePath), ct);
            if (!result.IsMalware)
                return;

            Console.WriteLine($"NetworkRealTimeProtection: threat on network – '{executablePath}' (score {result.MalwareScore:F2})");

            string processName;
            try { processName = Process.GetProcessById(pid).ProcessName; }
            catch { processName = Path.GetFileNameWithoutExtension(executablePath); }

            await ProcessControl.HandleThreatAsync(
                quarantine, settings.AppSettings, events, alertService,
                result, pid, executablePath, processName,
                "NetworkRealTimeProtection");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Console.WriteLine($"NetworkRealTimeProtection: error processing '{executablePath}' – {ex.Message}");
        }
    }

    // -----------------------------------------------------------------------
    // netstat helpers
    // -----------------------------------------------------------------------

    private static async Task<string> RunNetstatAsync(CancellationToken ct)
    {
        var psi = new ProcessStartInfo("netstat", "-ano")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var proc = Process.Start(psi);
        if (proc == null) return string.Empty;

        string output = await proc.StandardOutput.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);
        return output;
    }

    private static IEnumerable<int> ParsePids(string output)
    {
        var seen = new HashSet<int>();
        foreach (Match m in NetstatPidRe.Matches(output))
        {
            if (int.TryParse(m.Groups[1].Value, out int pid) && pid > 0 && seen.Add(pid))
                yield return pid;
        }
    }

    // -----------------------------------------------------------------------

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cts?.Cancel();
        _cts?.Dispose();

        GC.SuppressFinalize(this);
    }
}
