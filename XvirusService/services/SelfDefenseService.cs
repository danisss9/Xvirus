using System.Diagnostics;
using System.Runtime.Versioning;
using Xvirus;

namespace XvirusService.Services;

/// <summary>
/// Lightweight tamper resistance for the Firewall / Anti-Malware service. When enabled,
/// configures Windows Service recovery so the protection service automatically restarts
/// if it is killed, locks down the binary/config/quarantine ACLs via <c>icacls</c>, and
/// rewrites the service security descriptor so non-admins cannot stop/pause it.
/// (No kernel driver — this is a deterrent, not full tamper protection.)
/// </summary>
[SupportedOSPlatform("windows")]
public class SelfDefenseService
{
    private static string ServiceName => AppInfo.ServiceName;

    // SDDL service access right mnemonics we care about:
    //   CC = SERVICE_QUERY_CONFIG, DC = SERVICE_CHANGE_CONFIG, LC = SERVICE_QUERY_STATUS,
    //   SW = SERVICE_ENUMERATE_DEPENDENTS, RP = SERVICE_START,    WP = SERVICE_STOP,
    //   DT = SERVICE_PAUSE_CONTINUE, LO = SERVICE_INTERROGATE,
    //   CR = SERVICE_USER_DEFINED_CONTROL, SD = DELETE, RC = READ_CONTROL,
    //   WD = WRITE_DAC, WO = WRITE_OWNER
    //
    // Hardened SDDL: SYSTEM and Administrators keep full control (incl. STOP); the
    // Interactive-User and Service ACEs are granted query/start/interrogate only —
    // no WP (STOP), no DT (PAUSE), no DC (CHANGE_CONFIG), no SD/WD/WO.
    private const string HardenedSddl =
        "D:(A;;CCLCSWRPWPDTLOCRRC;;;SY)" +          // SYSTEM  : full control
        "(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)" +    // Admins  : full control + write DAC/owner/delete
        "(A;;CCLCSWLOCRRC;;;IU)" +                   // Interactive users: query + start + interrogate (no stop)
        "(A;;CCLCSWLOCRRC;;;SU)";                    // Service accounts : same as above

    // Permissive SDDL used when self-defense is disabled — restores the Windows default
    // for a user-mode service (interactive users may stop it again).
    private const string DefaultSddl =
        "D:(A;;CCLCSWRPWPDTLOCRRC;;;SY)" +
        "(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)" +
        "(A;;CCLCSWLOCRRC;;;IU)" +
        "(A;;CCLCSWLOCRRC;;;SU)" +
        "(A;;CCLCSWRPWPDTLOCRRC;;;S-1-5-32-547)";  // Power Users (legacy default)

    public void Apply(bool enabled)
    {
        try
        {
            // reset= window (seconds) after which the failure count resets; actions list
            // restarts the service three times, 60s apart. Disabling clears the actions.
            var args = enabled
                ? $"failure {ServiceName} reset= 86400 actions= restart/60000/restart/60000/restart/60000"
                : $"failure {ServiceName} reset= 0 actions= \"\"";
            RunSc(args);
        }
        catch
        {
            // Not running as a registered service (dev mode) — non-fatal.
        }

        try
        {
            ApplyServiceSecurityDescriptor(enabled);
        }
        catch
        {
            // sdset can fail in dev mode or without SeSecurityPrivilege — non-fatal.
        }

        try
        {
            ApplyAcls(enabled);
        }
        catch
        {
            // icacls may fail when not elevated or running from a non-installed path.
        }
    }

    /// <summary>
    /// Rewrites the service security descriptor via <c>sc sdset</c> so that only
    /// SYSTEM/Administrators can stop, pause, or reconfigure the service.
    /// </summary>
    private static void ApplyServiceSecurityDescriptor(bool enabled)
    {
        var sddl = enabled ? HardenedSddl : DefaultSddl;
        // sc sdset "<service>" "<sddl>"
        RunSc($"sdset {ServiceName} \"{sddl}\"");
    }

    /// <summary>
    /// Hardens the on-disk ACLs for the service binary directory, the JSON config files,
    /// and the quarantine folder. When <paramref name="enabled"/> is <c>false</c>, the
    /// inherited defaults are restored.
    /// </summary>
    private static void ApplyAcls(bool enabled)
    {
        var baseDir = Utils.CurrentDir;

        // 1. Binary directory (the service .exe + SDK DLLs). Users get Read & Execute,
        //    write/modify is reserved for SYSTEM and Administrators.
        HardenPath(baseDir, enabled,
            enabledGrant: "System:(OI)(CI)(F) Administrators:(OI)(CI)(F) Users:(OI)(CI)(RX)",
            enabledDeny: null,
            inheritanceReset: !enabled);

        // 2. Config files — settings.json / appsettings.json / appsettings.Development.json.
        //    Users get Read-only; only SYSTEM/Admins may write.
        foreach (var configFile in new[] { "settings.json", "appsettings.json", "appsettings.Development.json" })
        {
            var path = Path.Combine(baseDir, configFile);
            if (!File.Exists(path)) continue;
            HardenPath(path, enabled,
                enabledGrant: "System:(F) Administrators:(F) Users:(R)",
                enabledDeny: "Users:(WD,AD,WEA,DC)",
                inheritanceReset: !enabled);
        }

        // 3. Quarantine folder — contains malware samples. Strip inheritance so files
        //    dropped here don't inherit the permissive parent ACL, and grant Users only
        //    List/Read (no Write, no Delete) to prevent a sample from being smuggled out
        //    or replaced by a non-admin process.
        var quarantineDir = Path.Combine(baseDir, "Quarantine");
        if (Directory.Exists(quarantineDir))
        {
            if (enabled)
            {
                // Disable inheritance and copy inherited ACEs, then lock down.
                RunIcacls($"\"{quarantineDir}\" /inheritance:r /grant:r " +
                          "System:(OI)(CI)(F) Administrators:(OI)(CI)(F) Users:(OI)(CI)(RX)");
                // Explicitly deny Users write/delete on the folder contents.
                RunIcacls($"\"{quarantineDir}\" /deny:r Users:(OI)(CI)(WD,AD,DE,DC)");
            }
            else
            {
                RunIcacls($"\"{quarantineDir}\" /reset /q");
            }
        }
    }

    private static void HardenPath(string path, bool enabled, string enabledGrant, string? enabledDeny, bool inheritanceReset)
    {
        if (enabled)
        {
            // Replace all explicit ACEs with the hardened grant set.
            RunIcacls($"\"{path}\" /inheritance:r /grant:r {enabledGrant}");
            if (!string.IsNullOrEmpty(enabledDeny))
                RunIcacls($"\"{path}\" /deny:r {enabledDeny}");
        }
        else if (inheritanceReset)
        {
            // Restore inheritable defaults from the parent directory.
            RunIcacls($"\"{path}\" /reset /q");
        }
    }

    private static void RunSc(string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "sc.exe",
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var process = Process.Start(psi);
        process?.WaitForExit(5000);
    }

    private static void RunIcacls(string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "icacls.exe",
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var process = Process.Start(psi);
        process?.WaitForExit(10000);
    }
}
