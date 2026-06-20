using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.Json;
using Xvirus;
using Xvirus.Model;
using XvirusService.Model;

namespace XvirusService.Services;

/// <summary>
/// Shared on-demand / scheduled scan driver. Walks a folder with the engine
/// <see cref="Scanner"/>, throttles <c>scan-progress</c> SSE updates, auto-quarantines
/// detections when enabled, and pushes <c>threat</c> + <c>scan-complete</c> events.
/// Used by both <see cref="Api.ScanApi"/> and <see cref="ScheduledScanService"/>.
/// </summary>
[SupportedOSPlatform("windows")]
public class ScanService(
    Scanner scanner,
    Quarantine quarantine,
    ServerEventService events,
    SettingsService settings,
    ThreatAlertService alertService)
{
    public async Task<ScanResultDTO> ScanAsync(string path, CancellationToken ct)
    {
        int files = 0, threats = 0;
        var sw = Stopwatch.StartNew();
        long lastEmit = 0;

        await Task.Run(() =>
        {
            // A single file (e.g. from the "Scan with Xvirus" context menu) scans just that
            // file; otherwise walk the folder. ScanFolder(path) registers under `path`, so
            // /scan/cancel → Scanner.CancelScan(path) stops it gracefully. We also honour
            // request abort via ct.
            if (File.Exists(path))
            {
                files++;
                var single = scanner.ScanFile(path);
                if (single.IsMalware) { threats++; HandleScanThreat(single); }
            }
            else
            {
                foreach (var result in scanner.ScanFolder(path))
                {
                    if (ct.IsCancellationRequested) break;
                    files++;

                    if (result.IsMalware)
                    {
                        threats++;
                        HandleScanThreat(result);
                    }

                    if (sw.ElapsedMilliseconds - lastEmit >= 200)
                    {
                        lastEmit = sw.ElapsedMilliseconds;
                        _ = events.SendAsync("scan-progress", JsonSerializer.Serialize(
                            new ScanProgressDTO { FilesScanned = files, ThreatsFound = threats, CurrentFile = result.Path },
                            AppJsonSerializerContext.Default.ScanProgressDTO));
                    }
                }
            }
        }, ct);

        var summary = new ScanResultDTO
        {
            FilesScanned = files,
            ThreatsFound = threats,
            Cancelled = ct.IsCancellationRequested,
        };

        await events.SendAsync("scan-complete", JsonSerializer.Serialize(
            summary, AppJsonSerializerContext.Default.ScanResultDTO));

        return summary;
    }

    private void HandleScanThreat(ScanResult result)
    {
        bool alreadyQuarantined = quarantine.GetFiles()
            .Any(q => string.Equals(q.OriginalFilePath, result.Path, StringComparison.OrdinalIgnoreCase));

        string action = "detected";
        if (settings.AppSettings.AutoQuarantine && !alreadyQuarantined)
        {
            try
            {
                quarantine.AddFile(result.Path);
                action = "quarantined";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ScanService: quarantine failed for '{result.Path}' – {ex.Message}");
                action = "quarantine-failed";
            }
        }

        Logger.LogHistory("threat", $"Scan: {result.Name} detected in '{result.Path}' (score {result.MalwareScore:P0}) – action: {action}");

        var evt = new ThreatEventDTO
        {
            FilePath = result.Path,
            FileName = Path.GetFileName(result.Path),
            ThreatName = result.Name,
            MalwareScore = result.MalwareScore,
            Action = action,
            ShowNotification = settings.AppSettings.ShowNotifications,
            AlreadyQuarantined = alreadyQuarantined,
        };

        // Detections that weren't auto-quarantined need a user decision — queue them so the
        // AlertView's quarantine/allow actions (POST /actions/{id}) can resolve them.
        if (action is "detected" or "quarantine-failed")
            alertService.AddPending(evt);

        _ = events.SendAsync("threat", JsonSerializer.Serialize(
            evt, AppJsonSerializerContext.Default.ThreatEventDTO));
    }
}
