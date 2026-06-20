namespace XvirusService.Model;

/// <summary>Request body for the /rules/allow and /rules/block endpoints.</summary>
public class RulePathDTO
{
    public required string Path { get; set; }
}
