namespace XvirusService;

public record ScanHistoryEntry(
    string Type,
    DateTime Timestamp,
    string Details,
    int FilesScanned = 0,
    int ThreatsFound = 0
);
