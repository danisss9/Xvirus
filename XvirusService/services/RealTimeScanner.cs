using System.Management;

namespace XvirusService.Services;

public class ProcessSpawnEventArgs : EventArgs
{
    public int ProcessId { get; set; }
    public string? ProcessName { get; set; }
    public string? CommandLine { get; set; }
    public DateTime Timestamp { get; set; }
    public string? ExecutablePath { get; set; }
}

public class RealTimeScanner : IDisposable
{
    private ManagementEventWatcher? _processWatcher;
    private readonly object _lockObject = new object();
    private bool _isDisposed;

    public event EventHandler<ProcessSpawnEventArgs>? ProcessSpawned;

    public void Start()
    {
        lock (_lockObject)
        {
            if (_processWatcher != null)
                return;

            try
            {
                var scope = new ManagementScope(@"\\.\root\cimv2");
                scope.Connect();

                var query = new WqlEventQuery("SELECT * FROM Win32_ProcessStartTrace");
                _processWatcher = new ManagementEventWatcher(scope, query);
                _processWatcher.EventArrived += OnProcessSpawned;
                _processWatcher.Start();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error starting RealTimeScanner: {ex.Message}");
                _processWatcher?.Dispose();
                _processWatcher = null;
            }
        }
    }

    public void Stop()
    {
        lock (_lockObject)
        {
            if (_processWatcher != null)
            {
                _processWatcher.Stop();
                _processWatcher.Dispose();
                _processWatcher = null;
            }
        }
    }

    private void OnProcessSpawned(object? sender, EventArrivedEventArgs e)
    {
        try
        {
            var managementBaseObject = e.NewEvent;

            var processId = Convert.ToInt32(managementBaseObject.Properties["ProcessID"].Value ?? 0);
            var processName = managementBaseObject.Properties["ProcessName"].Value?.ToString();
            var commandLine = managementBaseObject.Properties["CommandLine"].Value?.ToString();

            var args = new ProcessSpawnEventArgs
            {
                ProcessId = processId,
                ProcessName = Path.GetFileName(processName ?? "Unknown"),
                CommandLine = commandLine,
                ExecutablePath = processName,
                Timestamp = DateTime.UtcNow
            };

            ProcessSpawned?.Invoke(this, args);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error processing spawn event: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        lock (_lockObject)
        {
            Stop();
            _isDisposed = true;
        }

        GC.SuppressFinalize(this);
    }
}
