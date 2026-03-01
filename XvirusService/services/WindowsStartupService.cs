using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace XvirusService.Services;

[SupportedOSPlatform("windows")]
public class WindowsStartupService
{
    private const string ServiceName = "XvirusService";

    public void Apply(bool startWithWindows)
    {
        ApplyServiceStartType(startWithWindows);
        ApplyStartupRegistryEntry(startWithWindows);
    }

    private void ApplyServiceStartType(bool startWithWindows)
    {
        string startType = startWithWindows ? "auto" : "demand";
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = $"config {ServiceName} start= {startType}",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var process = Process.Start(psi);
            process?.WaitForExit(5000);
        }
        catch
        {
            // Not running as a registered service (e.g., development mode)
        }
    }

    private void ApplyStartupRegistryEntry(bool startWithWindows)
    {
        try
        {
            var installDir = AppContext.BaseDirectory.TrimEnd('\\', '/');
            var productName = Path.GetFileName(installDir);

            using var runKey = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
                writable: true);

            if (runKey == null) return;

            if (startWithWindows)
            {
                var uiExe = Directory
                    .GetFiles(installDir, "Xvirus*.exe")
                    .FirstOrDefault(f => !f.EndsWith("XvirusService.exe", StringComparison.OrdinalIgnoreCase));

                if (uiExe != null)
                    runKey.SetValue(productName, uiExe);
            }
            else
            {
                runKey.DeleteValue(productName, throwOnMissingValue: false);
            }
        }
        catch
        {
            // Registry not accessible (e.g., development mode or insufficient privileges)
        }
    }
}
