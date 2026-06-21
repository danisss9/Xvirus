using System.Diagnostics;
using System.Runtime.Versioning;
using System.Threading;
using Xvirus;

namespace XvirusService.Services;

[SupportedOSPlatform("windows")]
public class NetworkRealTimeProtection(
    SettingsService settings,
    Scanner scanner,
    Quarantine quarantine,
    ServerEventService events,
    ThreatAlertService alertService,
    Rules rules) : IDisposable
{
    // 750ms — between the 500ms floor and 1s ceiling from the todo. Fast enough to
    // catch a 1s-lived connection that the old 3s netstat poll routinely missed,
    // without burning noticeable CPU on GetExtendedTcpTable/UdpTable (typically
    // <1ms for a few hundred endpoints).
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(750);

    private CancellationTokenSource? _cts;
    private Task? _monitorTask;
    private EtwNetworkListener? _etwListener;

    // Paths already scanned this session – avoids re-scanning the same
    // executable every poll tick. Keyed on full path (case-insensitive).
    private readonly HashSet<string> _scannedPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _scannedLock = new();

    // PIDs we've already handed off to a scan task this session, so a process
    // that holds many connections is only scanned once per session (matching
    // the previous netstat-based behavior).
    private readonly HashSet<int> _scannedPids = new();
    private readonly object _pidLock = new();

    private bool _disposed;

    // -----------------------------------------------------------------------

    public void Start()
    {
        if (!settings.AppSettings.NetworkProtection)
        {
            Console.WriteLine("NetworkRealTimeProtection: disabled in settings, not starting.");
            return;
        }

        Console.WriteLine("NetworkRealTimeProtection: starting IPHelper network connection monitor...");

        // Start the ETW Kernel-Network listener for sub-100ms PID notification.
        // If it can't start (not elevated, provider unavailable), we silently
        // fall back to the IPHelper poll alone.
        _etwListener = new EtwNetworkListener(OnEtwNetworkPid);
        if (_etwListener.Start("XvirusNetworkMonitor"))
            Console.WriteLine("NetworkRealTimeProtection: ETW Kernel-Network listener active.");
        else
            Console.WriteLine("NetworkRealTimeProtection: ETW listener unavailable — relying on IPHelper poll.");

        _cts = new CancellationTokenSource();
        _monitorTask = Task.Run(() => MonitorLoopAsync(_cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        _etwListener?.Stop();
        Console.WriteLine("NetworkRealTimeProtection: stopped.");
    }

    // -----------------------------------------------------------------------
    // ETW fast-path — called from the ETW processing thread
    // -----------------------------------------------------------------------

    /// <summary>
    /// Invoked by <see cref="EtwNetworkListener"/> the moment the kernel reports
    /// a TCP/UDP operation for a PID we haven't scanned yet. This is the
    /// "near-real-time" path; the IPHelper poll is the safety net.
    /// </summary>
    private void OnEtwNetworkPid(int pid)
    {
        // Claim the PID under the lock; do all scan work outside the lock.
        string? path;
        lock (_pidLock)
        {
            if (!_scannedPids.Add(pid)) return;
            path = ProcessControl.ResolveProcessPath(pid);
        }

        if (string.IsNullOrEmpty(path)) return;

        lock (_scannedLock)
        {
            if (!_scannedPaths.Add(path)) return;
        }

        _ = HandleConnectionAsync(pid, path, CancellationToken.None);
    }

    // -----------------------------------------------------------------------
    // Poll loop — IPHelper snapshot every PollInterval
    // -----------------------------------------------------------------------

    private async Task MonitorLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(PollInterval);

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
            // GetConnectedPids is a single in-process IPHelper call (no process spawn,
            // no text parsing) — this is what makes the 750ms cadence affordable.
            HashSet<int> pids;
            try { pids = IpHelper.GetConnectedPids(); }
            catch (Exception ex)
            {
                Console.WriteLine($"NetworkRealTimeProtection: IPHelper snapshot failed – {ex.Message}");
                return;
            }

            // Claim the PIDs we haven't scanned yet under the lock, then release the
            // lock before doing any scan work so concurrent ticks don't block.
            List<(int pid, string path)> toScan = new();
            lock (_pidLock)
            {
                foreach (var pid in pids)
                {
                    if (!_scannedPids.Add(pid)) continue;

                    string? path = ProcessControl.ResolveProcessPath(pid);
                    if (string.IsNullOrEmpty(path)) continue;

                    lock (_scannedLock)
                    {
                        if (!_scannedPaths.Add(path))
                        {
                            // Already scanned this binary via another PID — skip but
                            // keep the PID marked so we don't keep resolving it.
                            continue;
                        }
                    }

                    toScan.Add((pid, path));
                }
            }

            foreach (var (pid, path) in toScan)
            {
                if (ct.IsCancellationRequested) return;
                // Fire-and-forget per process so we don't block the poll tick
                _ = HandleConnectionAsync(pid, path, ct);
            }

            await Task.CompletedTask;
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

            // Firewall product: cut the program's network access immediately via a
            // persisted block rule (enforced through netsh) so it can't reconnect.
            if (settings.IsFirewall)
            {
                try { rules.AddBlockRule(executablePath); }
                catch (Exception ex) { Console.WriteLine($"NetworkRealTimeProtection: failed to block '{executablePath}' – {ex.Message}"); }
            }

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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cts?.Cancel();
        _cts?.Dispose();
        _etwListener?.Dispose();

        GC.SuppressFinalize(this);
    }
}
