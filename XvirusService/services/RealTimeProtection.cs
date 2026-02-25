using System.Management;
using System.Runtime.Versioning;
using Xvirus;

namespace XvirusService.Services;

[SupportedOSPlatform("windows")]
public class RealTimeProtection(
    SettingsService settings,
    Scanner scanner,
    Quarantine quarantine,
    ServerEventService events,
    ThreatAlertService alertService) : IDisposable
{
    private ManagementEventWatcher? _watcher;
    private readonly object _lock = new();
    private bool _disposed;

    // -----------------------------------------------------------------------

    public void Start()
    {
        if (!settings.AppSettings.RealTimeProtection)
        {
            Console.WriteLine("RealTimeProtection: disabled in settings, not starting.");
            return;
        }

        Console.WriteLine("RealTimeProtection: starting process monitor...");

        try
        {
            var query = new WqlEventQuery("SELECT * FROM Win32_ProcessStartTrace");
            lock (_lock)
            {
                _watcher = new ManagementEventWatcher(query);
                _watcher.EventArrived += OnProcessSpawned;
                _watcher.Start();
            }

            Console.WriteLine("RealTimeProtection: process monitor active.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"RealTimeProtection: failed to start – {ex.Message}");
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (_watcher == null) return;
            try { _watcher.Stop(); } catch { /* ignore */ }
            _watcher.EventArrived -= OnProcessSpawned;
        }

        Console.WriteLine("RealTimeProtection: stopped.");
    }

    // -----------------------------------------------------------------------
    // WMI event handler – runs on a WMI thread pool thread
    // -----------------------------------------------------------------------

    private void OnProcessSpawned(object sender, EventArrivedEventArgs e)
    {
        try
        {
            var props = e.NewEvent.Properties;
            uint pid = Convert.ToUInt32(props["ProcessID"]?.Value ?? 0u);
            string processName = props["ProcessName"]?.Value?.ToString() ?? string.Empty;

            if (pid == 0) return;

            // Offload to thread-pool so we never block the WMI callback thread
            Task.Run(() => HandleProcessAsync((int)pid, processName));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"RealTimeProtection: error in WMI callback – {ex.Message}");
        }
    }

    // -----------------------------------------------------------------------

    private async Task HandleProcessAsync(int pid, string processName)
    {
        try
        {
            string? executablePath = ProcessControl.ResolveProcessPath(pid);
            if (string.IsNullOrEmpty(executablePath))
                return;

            var result = await Task.Run(() => scanner.ScanFile(executablePath));
            if (!result.IsMalware)
                return;

            Console.WriteLine($"RealTimeProtection: threat detected – '{executablePath}' (score {result.MalwareScore:F2})");

            await ProcessControl.HandleThreatAsync(
                quarantine, settings.AppSettings, events, alertService,
                result, pid, executablePath, processName,
                "RealTimeProtection");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"RealTimeProtection: error processing pid {pid} – {ex.Message}");
        }
    }

    // -----------------------------------------------------------------------

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        lock (_lock)
        {
            _watcher?.Dispose();
            _watcher = null;
        }

        GC.SuppressFinalize(this);
    }
}
