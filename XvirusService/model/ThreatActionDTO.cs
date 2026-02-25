namespace XvirusService.Model;

public class ThreatActionDTO
{
    /// <summary>"quarantine" kills the process and moves the file; "allow" resumes the suspended process.</summary>
    public required string Action { get; set; }
    /// <summary>If true, a permanent allow or block rule is created for the file path.</summary>
    public bool RememberDecision { get; set; }
}
