using Xvirus;
using XvirusService.Model;

namespace XvirusService.Services;

/// <summary>
/// In-memory store for threats that require user action (process is suspended,
/// not yet quarantined). The UI polls <c>GET /actions/pending</c> on startup
/// and responds via <c>POST /actions/{id}</c>.
/// </summary>
public class ThreatAlertService(Quarantine quarantine, Rules rules)
{
    private readonly Dictionary<string, ThreatEventDTO> _pending = new();
    private readonly object _lock = new();

    public void AddPending(ThreatEventDTO threat)
    {
        lock (_lock) _pending[threat.Id] = threat;
    }

    public List<ThreatEventDTO> GetPending()
    {
        lock (_lock) return [.. _pending.Values];
    }

    /// <summary>
    /// Executes the user's chosen action and removes the alert from the pending list.
    /// Returns <c>false</c> if the id is not found.
    /// </summary>
    public bool RespondToAlert(string id, string action, bool rememberDecision = false)
    {
        ThreatEventDTO? threat;
        lock (_lock)
        {
            if (!_pending.TryGetValue(id, out threat)) return false;
            _pending.Remove(id);
        }

        if (action == "quarantine")
        {
            ProcessControl.Kill(threat.ProcessId, "ThreatAlertService");
            try { quarantine.AddFile(threat.FilePath); }
            catch (Exception ex)
            {
                Console.WriteLine($"ThreatAlertService: quarantine failed for '{threat.FilePath}' – {ex.Message}");
                ProcessControl.ScheduleQuarantineOnReboot(quarantine, threat.FilePath, "ThreatAlertService");
            }
            if (rememberDecision) rules.AddBlockRule(threat.FilePath);
        }
        else if (action == "allow")
        {
            ProcessControl.Resume(threat.ProcessId, "ThreatAlertService");
            if (rememberDecision) rules.AddAllowRule(threat.FilePath);
        }

        return true;
    }
}
