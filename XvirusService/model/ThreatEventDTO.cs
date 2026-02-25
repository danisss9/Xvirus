namespace XvirusService.Model;

public class ThreatEventDTO
{
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ProcessName { get; set; } = string.Empty;
    public int ProcessId { get; set; }
    public string ThreatName { get; set; } = string.Empty;
    public double MalwareScore { get; set; }
    /// <summary>"quarantined" | "suspended"</summary>
    public string Action { get; set; } = string.Empty;
    /// <summary>Whether the UI should display a desktop notification.</summary>
    public bool ShowNotification { get; set; }
    /// <summary>Whether this file already had an active quarantine entry when the threat was detected.</summary>
    public bool AlreadyQuarantined { get; set; }
}
