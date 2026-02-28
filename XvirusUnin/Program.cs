using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

// P/Invoke declarations for MessageBox and window manipulation
internal static class NativeInterop
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

    [DllImport("kernel32.dll")]
    public static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    public const uint MB_YESNO = 0x04;
    public const uint MB_ICONQUESTION = 0x20;
    public const uint MB_ICONERROR = 0x10;
    public const int IDYES = 6;
    public const int IDNO = 7;
    public const int SW_HIDE = 0;
}

internal record ProductConfig(
    string ProductName,
    string ServiceName,
    string UiProcessName,
    string InstallFolderName,
    string RegistryKey
)
{
    public static ProductConfig FromMode(string mode) => mode switch
    {
        "am" => new ProductConfig(
            ProductName: "Xvirus Anti-Malware",
            ServiceName: "XvirusAntiMalwareService",
            UiProcessName: "XvirusAM",
            InstallFolderName: "Xvirus Anti-Malware",
            RegistryKey: "Xvirus Anti-Malware"
        ),
        "fw" => new ProductConfig(
            ProductName: "Xvirus Firewall",
            ServiceName: "XvirusFirewallService",
            UiProcessName: "XvirusFW",
            InstallFolderName: "Xvirus Firewall",
            RegistryKey: "Xvirus Firewall"
        ),
        _ => throw new ArgumentException($"Unknown mode '{mode}'. Expected 'am' or 'fw'.")
    };
}

internal class Program
{
    [SupportedOSPlatform("windows")]
    static void Main(string[] args)
    {
        try
        {
            // Hide console window and taskbar icon if a console window exists
            var consoleHandle = NativeInterop.GetConsoleWindow();
            if (consoleHandle != IntPtr.Zero)
            {
                NativeInterop.ShowWindow(consoleHandle, NativeInterop.SW_HIDE);
            }

            // Parse mode argument: "am" or "fw"
            var mode = args.Length > 0 ? args[0].ToLowerInvariant() : "";
            ProductConfig config;
            try
            {
                config = ProductConfig.FromMode(mode);
            }
            catch (ArgumentException ex)
            {
                _ = NativeInterop.MessageBox(
                    IntPtr.Zero,
                    ex.Message,
                    "Uninstall Error",
                    NativeInterop.MB_ICONERROR
                );
                Environment.Exit(1);
                return;
            }

            // Show confirmation dialog
            int result = NativeInterop.MessageBox(
                IntPtr.Zero,
                $"Are you sure you want to uninstall {config.ProductName}? All application files and settings will be removed.\n\nThis action cannot be undone.",
                $"Uninstall {config.ProductName}",
                NativeInterop.MB_YESNO | NativeInterop.MB_ICONQUESTION
            );

            if (result != NativeInterop.IDYES)
            {
                return;
            }

            // Kill UI process if running
            Console.WriteLine($"Stopping {config.ProductName} application...");
            KillUiProcess(config);

            // Stop and delete Windows Service
            Console.WriteLine("Stopping and removing Windows Service...");
            StopAndDeleteService(config);

            // Delete registry entries
            Console.WriteLine("Removing registry entries...");
            DeleteRegistryEntries(config);

            // Delete shortcuts
            Console.WriteLine("Removing shortcuts...");
            DeleteShortcuts(config);

            // Schedule folder and self-deletion
            Console.WriteLine("Scheduling cleanup...");
            ScheduleSelfDeletion(config);

            // Show success message
            _ = NativeInterop.MessageBox(
                IntPtr.Zero,
                $"{config.ProductName} has been successfully uninstalled.",
                "Uninstall Complete",
                NativeInterop.MB_ICONQUESTION
            );
        }
        catch (Exception ex)
        {
            _ = NativeInterop.MessageBox(
                IntPtr.Zero,
                $"An error occurred during uninstallation:\n\n{ex.Message}",
                "Uninstall Error",
                NativeInterop.MB_ICONERROR
            );
            Environment.Exit(1);
        }
    }

    static void KillUiProcess(ProductConfig config)
    {
        try
        {
            var processes = Process.GetProcessesByName(config.UiProcessName);
            foreach (var process in processes)
            {
                try
                {
                    process.Kill();
                    process.WaitForExit(5000);
                    Console.WriteLine($"Killed process: {process.ProcessName} (PID: {process.Id})");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning: Could not kill process {process.ProcessName}: {ex.Message}");
                }
                finally
                {
                    process.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Could not terminate UI process: {ex.Message}");
        }
    }

    static void StopAndDeleteService(ProductConfig config)
    {
        try
        {
            RunCommand("sc.exe", "stop " + config.ServiceName, ignoreErrors: true);
            System.Threading.Thread.Sleep(2000);
            RunCommand("sc.exe", "delete " + config.ServiceName, ignoreErrors: true);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Could not remove service: {ex.Message}");
        }
    }

    [SupportedOSPlatform("windows")]
    static void DeleteRegistryEntries(ProductConfig config)
    {
        try
        {
            using var baseKey = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                writable: true);

            baseKey?.DeleteSubKeyTree(config.RegistryKey, throwOnMissingSubKey: false);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Could not remove registry entries: {ex.Message}");
        }
    }

    static void DeleteShortcuts(ProductConfig config)
    {
        try
        {
            var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
            var startMenuPath = Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms);

            var lnkPath = Path.Combine(desktopPath, config.ProductName + ".lnk");
            if (File.Exists(lnkPath))
                File.Delete(lnkPath);

            var menuPath = Path.Combine(startMenuPath, config.ProductName);
            if (Directory.Exists(menuPath))
                Directory.Delete(menuPath, recursive: true);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Could not remove shortcuts: {ex.Message}");
        }
    }

    static void ScheduleSelfDeletion(ProductConfig config)
    {
        try
        {
            var installFolder = AppContext.BaseDirectory.TrimEnd('\\', '/');
            var folderName = Path.GetFileName(installFolder);

            // Safety check: only delete folders with expected name
            if (folderName != config.InstallFolderName)
            {
                Console.WriteLine($"Warning: Install folder name '{folderName}' doesn't match expected '{config.InstallFolderName}'. Skipping folder deletion.");
                return;
            }

            var batPath = Path.Combine(Path.GetTempPath(), $"xvirus_unin_{Guid.NewGuid():N}.bat");

            var batContent = $@"@echo off
rem Xvirus self-deletion script
timeout /t 2 /nobreak > nul
rmdir /s /q ""{installFolder}"" 2>nul
del /f /q ""%~f0"" 2>nul
";

            File.WriteAllText(batPath, batContent);

            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{batPath}\"",
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true,
                UseShellExecute = true
            };

            Process.Start(psi);
            Console.WriteLine("Cleanup scheduled. Application will exit now.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Could not schedule folder deletion: {ex.Message}");
        }
    }

    static void RunCommand(string fileName, string arguments, bool ignoreErrors = false)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(psi);
            process?.WaitForExit(5000);
        }
        catch (Exception ex)
        {
            if (!ignoreErrors) throw;
            Console.WriteLine($"Warning: Command failed: {ex.Message}");
        }
    }
}
