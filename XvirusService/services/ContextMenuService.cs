using System.Runtime.Versioning;
using Microsoft.Win32;

namespace XvirusService.Services;

/// <summary>
/// Registers / removes the Explorer right-click entry "Scan with Xvirus" for files
/// and folders, driven by <see cref="Xvirus.Model.AppSettingsDTO.EnableContextMenu"/>.
/// The entry launches the product UI with <c>--scan "&lt;path&gt;"</c>, which kicks off
/// an on-demand scan of the selected item.
/// </summary>
[SupportedOSPlatform("windows")]
public class ContextMenuService
{
    private const string KeyName = "XvirusScan";
    private const string MenuText = "Scan with Xvirus";

    public void Apply(bool enabled)
    {
        try
        {
            if (enabled) Register();
            else Unregister();
        }
        catch
        {
            // Registry not writable (dev mode / insufficient privileges) — non-fatal.
        }
    }

    private void Register()
    {
        var uiExe = ResolveUiExe();
        if (uiExe == null) return;

        // "*" = all files, "Directory" = folders.
        foreach (var scope in new[] { "*", "Directory" })
        {
            using var shellKey = Registry.ClassesRoot.CreateSubKey($@"{scope}\shell\{KeyName}");
            shellKey.SetValue(string.Empty, MenuText);
            shellKey.SetValue("Icon", $"\"{uiExe}\",0");
            using var cmdKey = shellKey.CreateSubKey("command");
            cmdKey.SetValue(string.Empty, $"\"{uiExe}\" --scan \"%1\"");
        }
    }

    private static void Unregister()
    {
        foreach (var scope in new[] { "*", "Directory" })
        {
            try { Registry.ClassesRoot.DeleteSubKeyTree($@"{scope}\shell\{KeyName}", throwOnMissingSubKey: false); }
            catch { /* ignore */ }
        }
    }

    private static string? ResolveUiExe()
    {
        var installDir = AppContext.BaseDirectory.TrimEnd('\\', '/');
        return Directory.GetFiles(installDir, "Xvirus*.exe")
            .FirstOrDefault(f => !f.EndsWith("XvirusService.exe", StringComparison.OrdinalIgnoreCase));
    }
}
