using System.Diagnostics;

namespace XvirusService.Services;

public class WindowsStartupService
{
    private const string ServiceName = "XvirusService";

    public void Apply(bool startWithWindows)
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
}
