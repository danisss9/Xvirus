using System;

namespace XvirusService.Model
{
    public class ProcessSpawnEventArgs : EventArgs
    {
        public int ProcessId { get; set; }
        public string? ProcessName { get; set; }
        public string? CommandLine { get; set; }
        public DateTime Timestamp { get; set; }
        public string? ExecutablePath { get; set; }
    }
}
