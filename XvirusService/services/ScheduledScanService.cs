using System.Runtime.Versioning;

namespace XvirusService.Services;

/// <summary>
/// Runs a full system scan on the cadence chosen in
/// <see cref="Xvirus.Model.AppSettingsDTO.ScheduledScan"/> (off / daily / weekly / monthly).
/// The last-run timestamp is persisted next to the executable so the schedule
/// survives service restarts and reboots.
/// </summary>
[SupportedOSPlatform("windows")]
public class ScheduledScanService(SettingsService settings, ScanService scan) : BackgroundService
{
    private static readonly string MarkerPath =
        Path.Combine(AppContext.BaseDirectory, "scheduledscan.txt");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Re-evaluate every 30 minutes; cheap and responsive to settings changes.
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(30));

        try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
        catch (OperationCanceledException) { return; }

        do
        {
            try { await MaybeRunAsync(stoppingToken); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { Console.WriteLine($"ScheduledScan: {ex.Message}"); }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task MaybeRunAsync(CancellationToken ct)
    {
        var cadence = (settings.AppSettings.ScheduledScan ?? "off").ToLowerInvariant();
        var interval = cadence switch
        {
            "daily" => TimeSpan.FromDays(1),
            "weekly" => TimeSpan.FromDays(7),
            "monthly" => TimeSpan.FromDays(30),
            _ => TimeSpan.Zero,
        };
        if (interval == TimeSpan.Zero) return; // "off" or unknown

        var last = ReadLastRun();
        if (last == null)
        {
            // First time we observe an enabled schedule — anchor from now so we
            // don't kick off a surprise full-disk scan immediately after install.
            WriteLastRun(DateTime.UtcNow);
            return;
        }

        if (DateTime.UtcNow - last.Value < interval) return;

        Console.WriteLine($"ScheduledScan: starting {cadence} scan.");
        await scan.ScanAsync("C:\\", ct);
        WriteLastRun(DateTime.UtcNow);
    }

    private static DateTime? ReadLastRun()
    {
        try
        {
            if (File.Exists(MarkerPath) &&
                long.TryParse(File.ReadAllText(MarkerPath).Trim(), out var ticks))
                return new DateTime(ticks, DateTimeKind.Utc);
        }
        catch { /* treat unreadable marker as "never run" */ }
        return null;
    }

    private static void WriteLastRun(DateTime when)
    {
        try { File.WriteAllText(MarkerPath, when.Ticks.ToString()); }
        catch { /* best effort */ }
    }
}
