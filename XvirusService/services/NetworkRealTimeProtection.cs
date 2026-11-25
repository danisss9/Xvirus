using System.Diagnostics;
using System.Runtime.Versioning;
using Xvirus;
using Xvirus.Model;

namespace XvirusService.Services;

[SupportedOSPlatform("windows")]
public class NetworkRealTimeProtection : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(750);
    private const int MaxCachedPids = 4096;

    private readonly SettingsService _settings;
    private readonly Scanner _scanner;
    private readonly Quarantine _quarantine;
    private readonly ServerEventService _events;
    private readonly ThreatAlertService _alertService;
    private readonly Rules _rules;

    private readonly object _lifecycleLock = new();
    private CancellationTokenSource? _cts;
    private Task? _monitorTask;
    private EtwNetworkListener? _etwListener;
    private volatile bool _running;

    // Paths already scanned this session — invalidated when rules change.
    private readonly HashSet<string> _scannedPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _scannedLock = new();

    // PIDs already inspected, keyed on (pid, startTimeTicks) so Windows PID reuse
    // never skips a brand-new process that received a recycled PID.
    private readonly HashSet<(int pid, long startTicks)> _scannedPids = new();
    private readonly object _pidLock = new();

    private bool _disposed;

    public bool IsRunning => _running;

    public NetworkRealTimeProtection(
        SettingsService settings,
        Scanner scanner,
        Quarantine quarantine,
        ServerEventService events,
        ThreatAlertService alertService,
        Rules rules)
    {
        _settings = settings;
        _scanner = scanner;
        _quarantine = quarantine;
        _events = events;
        _alertService = alertService;
        _rules = rules;

        _rules.RulesChanged += OnRuleChanged;
    }

    // -----------------------------------------------------------------------

    public void Start()
    {
        lock (_lifecycleLock)
        {
            if (_running) return;

            if (!_settings.AppSettings.NetworkProtection)
            {
                Console.WriteLine("NetworkRealTimeProtection: disabled in settings, not starting.");
                return;
            }

            Console.WriteLine("NetworkRealTimeProtection: starting IPHelper + ETW network monitor...");

            var sessionName = $"{AppInfo.EtwSessionPrefix}Network";
            _etwListener = new EtwNetworkListener(OnEtwNetworkPid);
            if (_etwListener.Start(sessionName))
                Console.WriteLine($"NetworkRealTimeProtection: ETW Kernel-Network listener active ({sessionName}).");
            else
                Console.WriteLine("NetworkRealTimeProtection: ETW listener unavailable — relying on IPHelper poll.");

            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            _running = true;
            _monitorTask = Task.Run(() => MonitorLoopAsync(ct));
        }
    }

    public void Stop()
    {
        CancellationTokenSource? cts;
        EtwNetworkListener? etw;
        Task? monitorTask;

        lock (_lifecycleLock)
        {
            if (!_running) return;
            _running = false;
            cts = _cts;
            _cts = null;
            etw = _etwListener;
            _etwListener = null;
            monitorTask = _monitorTask;
            _monitorTask = null;
        }

        cts?.Cancel();
        etw?.Dispose();
        try { monitorTask?.Wait(1500); } catch { /* best effort */ }
        cts?.Dispose();

        // Reset session caches so re-enabling protection performs a fresh pass.
        lock (_pidLock) _scannedPids.Clear();
        lock (_scannedLock) _scannedPaths.Clear();

        Console.WriteLine("NetworkRealTimeProtection: stopped.");
    }

    /// <summary>Starts or stops monitoring to match the latest <see cref="SettingsService.AppSettings"/>.</summary>
    public void ApplySettings()
    {
        if (_settings.AppSettings.NetworkProtection)
            Start();
        else
            Stop();
    }

    /// <summary>Removes a path from the session scan cache so it will be re-evaluated on next activity.</summary>
    public void InvalidatePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        lock (_scannedLock) _scannedPaths.Remove(path.Trim());
    }

    private void OnRuleChanged(Rule rule)
    {
        if (rule.Kind == RuleKind.Program)
            InvalidatePath(rule.Path);
    }

    // -----------------------------------------------------------------------
    // ETW fast-path — called from the ETW processing thread
    // -----------------------------------------------------------------------

    private void OnEtwNetworkPid(int pid)
    {
        if (!_running) return;

        string? path;
        if (!TryClaimPid(pid, out path) || string.IsNullOrEmpty(path))
            return;

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
            HashSet<int> pids;
            try { pids = IpHelper.GetConnectedPids(); }
            catch (Exception ex)
            {
                Console.WriteLine($"NetworkRealTimeProtection: IPHelper snapshot failed – {ex.Message}");
                return;
            }

            List<(int pid, string path)> toScan = new();
            foreach (var pid in pids)
            {
                if (!TryClaimPid(pid, out var path) || string.IsNullOrEmpty(path))
                    continue;

                lock (_scannedLock)
                {
                    if (!_scannedPaths.Add(path))
                        continue;
                }

                toScan.Add((pid, path));
            }

            foreach (var (pid, path) in toScan)
            {
                if (ct.IsCancellationRequested) return;
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

    private bool TryClaimPid(int pid, out string? path)
    {
        path = null;
        long startTicks = GetProcessStartTicks(pid);

        lock (_pidLock)
        {
            if (_scannedPids.Count >= MaxCachedPids)
                _scannedPids.Clear();

            if (!_scannedPids.Add((pid, startTicks)))
                return false;
        }

        path = ProcessControl.ResolveProcessPath(pid);
        return !string.IsNullOrEmpty(path);
    }

    private static long GetProcessStartTicks(int pid)
    {
        try
        {
            using var proc = Process.GetProcessById(pid);
            return proc.StartTime.ToUniversalTime().Ticks;
        }
        catch
        {
            return 0;
        }
    }

    // -----------------------------------------------------------------------
    // Per-process scan → threat response
    // -----------------------------------------------------------------------

    private async Task HandleConnectionAsync(int pid, string executablePath, CancellationToken ct)
    {
        try
        {
            var result = await Task.Run(() => _scanner.ScanFile(executablePath), ct);
            if (!result.IsMalware)
                return;

            Console.WriteLine($"NetworkRealTimeProtection: threat on network – '{executablePath}' (score {result.MalwareScore:F2})");

            // Firewall product in automatic mode: block the program's network access
            // immediately AND proceed to kill & quarantine via ProcessControl.HandleThreatAsync.
            // In "ask" mode, defer persisting a permanent block rule until the user responds
            // so choosing "Allow (once)" does not leave the app permanently blocked.
            if (_settings.IsFirewall && _settings.AppSettings.ThreatAction == "auto")
            {
                try { _rules.AddBlockRule(executablePath); }
                catch (Exception ex) { Console.WriteLine($"NetworkRealTimeProtection: failed to block '{executablePath}' – {ex.Message}"); }
            }

            string processName;
            try
            {
                using var proc = Process.GetProcessById(pid);
                processName = proc.ProcessName;
            }
            catch { processName = Path.GetFileNameWithoutExtension(executablePath); }

            await ProcessControl.HandleThreatAsync(
                _quarantine, _settings.AppSettings, _events, _alertService,
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
        _rules.RulesChanged -= OnRuleChanged;
        Stop();
        GC.SuppressFinalize(this);
    }
}
