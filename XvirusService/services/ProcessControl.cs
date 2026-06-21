using System.Runtime.InteropServices;
using System.Text;
using System.Runtime.Versioning;
using System.Text.Json;
using Xvirus;
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
    private const uint ProcessQueryInformation = 0x0400;
    private const uint ProcessVmRead = 0x0010;
    private const uint ProcessTerminate = 0x0001;
    private const uint ProcessSuspendResume = 0x0800;
    private const uint MoveFileDelayUntilReboot = 0x00000004;

    [DllImport("ntdll.dll")]
    private static extern int NtSuspendProcess(IntPtr processHandle);

    [DllImport("ntdll.dll")]
    private static extern int NtResumeProcess(IntPtr processHandle);

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(
        IntPtr processHandle,
        int processInformationClass,
        ref PROCESS_BASIC_INFORMATION processInformation,
        int processInformationLength,
        out int returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(
        IntPtr hProcess,
        IntPtr lpBaseAddress,
        [Out] byte[] lpBuffer,
        IntPtr nSize,
        out IntPtr lpNumberOfBytesRead);

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_BASIC_INFORMATION
    {
        public int ExitStatus;            // NTSTATUS (4) + 4 padding on x64
        public IntPtr PebBaseAddress;     // offset 8
        public IntPtr AffinityMask;       // offset 16
        public int BasePriority;          // offset 24
        public IntPtr UniqueProcessId;    // offset 32
        public IntPtr InheritedFromUniqueProcessId; // offset 40
    }

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

    /// <summary>
    /// Reads the command line of a running process by walking its PEB
    /// (PROCESS_BASIC_INFORMATION → PEB.ProcessParameters → CommandLine).
    /// Native-AOT safe — no WMI / System.Management reflection. Returns
    /// <c>null</c> when the process has exited or is inaccessible.
    /// </summary>
    internal static string? GetCommandLine(int pid)
    {
        IntPtr handle = OpenProcess(ProcessQueryInformation | ProcessVmRead, false, pid);
        if (handle == IntPtr.Zero)
        {
            // Fall back to limited info — some processes still expose the PEB
            // without VM read, but ReadProcessMemory will then fail gracefully.
            handle = OpenProcess(ProcessQueryLimitedInfo, false, pid);
            if (handle == IntPtr.Zero) return null;
        }

        try
        {
            var pbi = new PROCESS_BASIC_INFORMATION();
            int status = NtQueryInformationProcess(
                handle, 0 /* ProcessBasicInformation */, ref pbi,
                Marshal.SizeOf<PROCESS_BASIC_INFORMATION>(), out _);
            if (status != 0 || pbi.PebBaseAddress == IntPtr.Zero) return null;

            // PEB.ProcessParameters pointer is at offset 0x20 on x64.
            IntPtr processParameters = ReadIntPtr(handle, pbi.PebBaseAddress + 0x20);
            if (processParameters == IntPtr.Zero) return null;

            // RTL_USER_PROCESS_PARAMETERS.CommandLine is a UNICODE_STRING at
            // offset 0x70 on x64: { Length(2), MaxLength(2), [4 pad], Buffer(8) }.
            ushort length = ReadUshort(handle, processParameters + 0x70);
            if (length == 0) return null;
            IntPtr buffer = ReadIntPtr(handle, processParameters + 0x78);
            if (buffer == IntPtr.Zero) return null;

            byte[] bytes = new byte[length];
            if (!ReadProcessMemory(handle, buffer, bytes, (IntPtr)length, out _))
                return null;

            return Encoding.Unicode.GetString(bytes, 0, length);
        }
        catch
        {
            return null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private static IntPtr ReadIntPtr(IntPtr hProcess, IntPtr address)
    {
        byte[] buf = new byte[IntPtr.Size];
        if (!ReadProcessMemory(hProcess, address, buf, (IntPtr)IntPtr.Size, out _))
            return IntPtr.Zero;
        return IntPtr.Size == 8
            ? (IntPtr)BitConverter.ToInt64(buf, 0)
            : (IntPtr)BitConverter.ToInt32(buf, 0);
    }

    private static ushort ReadUshort(IntPtr hProcess, IntPtr address)
    {
        byte[] buf = new byte[2];
        return ReadProcessMemory(hProcess, address, buf, (IntPtr)2, out _)
            ? BitConverter.ToUInt16(buf, 0)
            : (ushort)0;
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
            string dest = Path.Combine(AppContext.BaseDirectory, "Quarantine", entry.QuarantinedFileName);

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

        Logger.LogHistory("threat", $"{source}: {result.Name} detected in '{executablePath}' (score: {result.MalwareScore:P0}) – action: {action}");

        // Store pending alert if user needs to take action
        if (action is "suspended" or "quarantine-failed")
            alertService.AddPending(evt);

        await events.SendAsync(
            "threat",
            JsonSerializer.Serialize(evt, AppJsonSerializerContext.Default.ThreatEventDTO));
    }
}
