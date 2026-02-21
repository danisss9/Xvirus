using System;

namespace Xvirus.Model
{
    public class HistoryEntry
    {
        public string Type { get; set; }
        public DateTime Timestamp { get; set; }
        public string Details { get; set; }

        // required for deserialization
        public HistoryEntry() { }

        public HistoryEntry(string type, DateTime timestamp, string details)
        {
            Type = type;
            Timestamp = timestamp;
            Details = details;
        }
    }
}

