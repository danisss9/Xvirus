using System.Runtime.InteropServices;
using System.Text;
using System.Runtime.Versioning;
using System.Text.Json;
using Xvirus.Model;
using XvirusService.Model;

namespace XvirusService.Services;

/// <summary>
/// Shared Windows API helpers and threat-response logic used by
/// <see cref="RealTimeProtection"/> and <see cref="NetworkRealTimeProtection"/>.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class ProcessControl
{
    private const uint ProcessQueryLimitedInfo = 0x1000;
    private const uint ProcessTerminate = 0x0001;
    private const uint ProcessSuspendResume = 0x0800;
    private const uint MoveFileDelayUntilReboot = 0x00000004;

    [DllImport("ntdll.dll")]
    private static extern int NtSuspendProcess(IntPtr processHandle);

    [DllImport("ntdll.dll")]
    private static extern int NtResumeProcess(IntPtr processHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(IntPtr hProcess, uint dwFlags, StringBuilder lpExeName, ref uint lpdwSize);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool MoveFileEx(string lpExistingFileName, string lpNewFileName, uint dwFlags);

    // -----------------------------------------------------------------------
    // Process helpers
    // -----------------------------------------------------------------------

    internal static string? ResolveProcessPath(int pid)
    {
        IntPtr handle = OpenProcess(ProcessQueryLimitedInfo, false, pid);
        if (handle == IntPtr.Zero) return null;
        try
        {
            var sb = new StringBuilder(1024);
            uint size = (uint)sb.Capacity;
            return QueryFullProcessImageName(handle, 0, sb, ref size) ? sb.ToString() : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    internal static void Kill(int pid, string source)
    {
        IntPtr handle = OpenProcess(ProcessTerminate, false, pid);
        if (handle == IntPtr.Zero)
        {
            Console.WriteLine($"{source}: failed to open process {pid} for termination (error {Marshal.GetLastWin32Error()}).");
            return;
        }
        try
        {
            if (!TerminateProcess(handle, 1))
                Console.WriteLine($"{source}: TerminateProcess failed for pid {pid} (error {Marshal.GetLastWin32Error()}).");
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    internal static void Suspend(int pid, string source)
    {
        IntPtr handle = OpenProcess(ProcessSuspendResume, false, pid);
        if (handle == IntPtr.Zero)
        {
            Console.WriteLine($"{source}: failed to open process {pid} for suspension (error {Marshal.GetLastWin32Error()}).");
            return;
        }

        try
        {
            int status = NtSuspendProcess(handle);
            if (status != 0)
                Console.WriteLine($"{source}: NtSuspendProcess returned 0x{status:X8} for pid {pid}.");
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    internal static void Resume(int pid, string source)
    {
        IntPtr handle = OpenProcess(ProcessSuspendResume, false, pid);
        if (handle == IntPtr.Zero)
        {
            Console.WriteLine($"{source}: failed to open process {pid} for resume (error {Marshal.GetLastWin32Error()}).");
            return;
        }

        try
        {
            int status = NtResumeProcess(handle);
            if (status != 0)
                Console.WriteLine($"{source}: NtResumeProcess returned 0x{status:X8} for pid {pid}.");
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    internal static bool ScheduleQuarantineOnReboot(Quarantine quarantine, string sourceFilePath, string source)
    {
        try
        {
            var entry = quarantine.RegisterPendingEntry(sourceFilePath);
            string dest = Path.Combine(AppContext.BaseDirectory, "quarantine", entry.QuarantinedFileName);

            if (!MoveFileEx(sourceFilePath, dest, MoveFileDelayUntilReboot))
            {
                Console.WriteLine($"{source}: MoveFileEx failed for '{sourceFilePath}' (error {Marshal.GetLastWin32Error()}).");
                return false;
            }

            Console.WriteLine($"{source}: '{sourceFilePath}' scheduled for quarantine on next reboot.");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{source}: failed to schedule quarantine on reboot for '{sourceFilePath}' – {ex.Message}");
            return false;
        }
    }

    // -----------------------------------------------------------------------
    // Shared threat response: kill/suspend + quarantine + SSE notification
    // -----------------------------------------------------------------------

    internal static async Task HandleThreatAsync(
        Quarantine quarantine,
        AppSettingsDTO appSettings,
        ServerEventService events,
        ThreatAlertService alertService,
        ScanResult result,
        int pid,
        string executablePath,
        string processName,
        string source)
    {
        bool alreadyQuarantined = quarantine.GetFiles()
            .Any(q => string.Equals(q.OriginalFilePath, executablePath, StringComparison.OrdinalIgnoreCase));

        string action;

        if (appSettings.ThreatAction == "auto" && !alreadyQuarantined)
        {
            Kill(pid, source);

            try
            {
                quarantine.AddFile(executablePath);
                action = "quarantined";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{source}: quarantine failed for '{executablePath}' – {ex.Message}. Scheduling move on reboot.");
                action = ScheduleQuarantineOnReboot(quarantine, executablePath, source)
                    ? "quarantine-pending-reboot"
                    : "quarantine-failed";
            }
        }
        else
        {
            Suspend(pid, source);
            action = "suspended";
        }

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

        // Store pending alert if user needs to take action
        if (action is "suspended" or "quarantine-failed")
            alertService.AddPending(evt);

        await events.SendAsync(
            "threat",
            JsonSerializer.Serialize(evt, AppJsonSerializerContext.Default.ThreatEventDTO));
    }
}
