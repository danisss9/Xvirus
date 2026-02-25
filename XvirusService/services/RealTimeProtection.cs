using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;
using XvirusService.Model;

namespace XvirusService.Services;

[SupportedOSPlatform("windows")]
public partial class RealTimeProtection(
    SettingsService settings,
    ScannerService scanner,
    Quarantine quarantine,
    ServerEventService events) : IDisposable
{
    private ManagementEventWatcher? _watcher;
    private readonly object _lock = new();
    private bool _disposed;

    // -----------------------------------------------------------------------
    // Windows API – process control
    // -----------------------------------------------------------------------

    private const uint ProcessSuspendResume = 0x0800;
    private const uint MoveFileDelayUntilReboot = 0x00000004;

    [DllImport("ntdll.dll")]
    private static extern int NtSuspendProcess(IntPtr processHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool MoveFileEx(string lpExistingFileName, string lpNewFileName, uint dwFlags);

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
    // Core detection → action → notify pipeline
    // -----------------------------------------------------------------------

    private async Task HandleProcessAsync(int pid, string processName)
    {
        try
        {
            string? executablePath = ResolveProcessPath(pid);
            if (string.IsNullOrEmpty(executablePath))
                return;

            // Scan on a thread-pool thread (CPU-bound)
            var result = await Task.Run(() => scanner.ScanFile(executablePath));
            if (!result.IsMalware)
                return;

            Console.WriteLine($"RealTimeProtection: threat detected – '{executablePath}' (score {result.MalwareScore:F2})");

            var appSettings = settings.AppSettings;

            bool alreadyQuarantined = quarantine.GetFiles()
                .Any(q => string.Equals(q.OriginalFilePath, executablePath, StringComparison.OrdinalIgnoreCase));

            string action;

            if (appSettings.AutoQuarantine && !alreadyQuarantined)
            {
                // Kill the process first, then move the file to quarantine
                KillProcess(pid);

                try
                {
                    quarantine.AddFile(executablePath);
                    action = "quarantined";
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"RealTimeProtection: quarantine failed for '{executablePath}' – {ex.Message}. Scheduling move on reboot.");
                    action = ScheduleQuarantineOnReboot(executablePath)
                        ? "quarantine-pending-reboot"
                        : "quarantine-failed";
                }
            }
            else
            {
                // Pause the process via NtSuspendProcess instead of terminating
                SuspendProcess(pid);
                action = "suspended";
            }

            // Notify the front-end via SSE
            var evt = new ThreatEventDTO
            {
                FilePath = executablePath,
                FileName = Path.GetFileName(executablePath),
                ProcessName = processName,
                ProcessId = pid,
                ThreatName = result.Name,
                MalwareScore = result.MalwareScore,
                Action = action,
                ShowNotification = appSettings.ShowNotifications,
                AlreadyQuarantined = alreadyQuarantined,
            };

            await events.SendAsync(
                "threat",
                JsonSerializer.Serialize(evt, AppJsonSerializerContext.Default.ThreatEventDTO));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"RealTimeProtection: error processing pid {pid} – {ex.Message}");
        }
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static string? ResolveProcessPath(int pid)
    {
        try
        {
            using var proc = Process.GetProcessById(pid);
            return proc.MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }

    private static void KillProcess(int pid)
    {
        try
        {
            using var proc = Process.GetProcessById(pid);
            proc.Kill();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"RealTimeProtection: failed to kill process {pid} – {ex.Message}");
        }
    }

    private bool ScheduleQuarantineOnReboot(string sourceFilePath)
    {
        try
        {
            var entry = quarantine.RegisterPendingEntry(sourceFilePath);
            string dest = Path.Combine(AppContext.BaseDirectory, "quarantine", entry.QuarantinedFileName);

            if (!MoveFileEx(sourceFilePath, dest, MoveFileDelayUntilReboot))
            {
                Console.WriteLine($"RealTimeProtection: MoveFileEx failed for '{sourceFilePath}' (error {Marshal.GetLastWin32Error()}).");
                return false;
            }

            Console.WriteLine($"RealTimeProtection: '{sourceFilePath}' scheduled for quarantine on next reboot.");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"RealTimeProtection: failed to schedule quarantine on reboot for '{sourceFilePath}' – {ex.Message}");
            return false;
        }
    }

    private static void SuspendProcess(int pid)
    {
        IntPtr handle = OpenProcess(ProcessSuspendResume, false, pid);
        if (handle == IntPtr.Zero)
        {
            Console.WriteLine($"RealTimeProtection: failed to open process {pid} for suspension (error {Marshal.GetLastWin32Error()}).");
            return;
        }

        try
        {
            int status = NtSuspendProcess(handle);
            if (status != 0)
                Console.WriteLine($"RealTimeProtection: NtSuspendProcess returned 0x{status:X8} for pid {pid}.");
        }
        finally
        {
            CloseHandle(handle);
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
