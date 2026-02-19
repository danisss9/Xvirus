namespace XvirusService;

public class ScanHistory
{
    private readonly List<ScanHistoryEntry> _entries = new();
    private readonly object _lock = new();

    public void AddEntry(ScanHistoryEntry entry)
    {
        lock (_lock)
        {
            _entries.Insert(0, entry); // Newest first
        }
    }

    public List<ScanHistoryEntry> GetEntries()
    {
        lock (_lock)
        {
            return new List<ScanHistoryEntry>(_entries);
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _entries.Clear();
        }
    }
}
