namespace XvirusService.Model;

/// <summary>Request body for /scan and /scan/cancel. Path defaults to the system drive.</summary>
public class ScanRequestDTO
{
    public string? Path { get; set; }
}

/// <summary>Final result returned by /scan and broadcast as the <c>scan-complete</c> SSE event.</summary>
public class ScanResultDTO
{
    public int FilesScanned { get; set; }
    public int ThreatsFound { get; set; }
    public bool Cancelled { get; set; }
}

/// <summary>Throttled progress pushed as the <c>scan-progress</c> SSE event.</summary>
public class ScanProgressDTO
{
    public int FilesScanned { get; set; }
    public int ThreatsFound { get; set; }
    public string CurrentFile { get; set; } = string.Empty;
}
