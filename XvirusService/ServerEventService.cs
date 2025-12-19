using System.Threading.Channels;

namespace XvirusService;

public class ServerEventService
{
    private readonly RealTimeScanner _scanner;

    public ServerEventService(RealTimeScanner scanner)
    {
        _scanner = scanner;
    }

    public IAsyncEnumerable<string> HandleEvents(HttpContext context, CancellationToken cancellationToken)
    {
        context.Response.Headers.Append("Content-Type", "text/event-stream");
        context.Response.Headers.Append("Cache-Control", "no-cache");
        context.Response.Headers.Append("Connection", "keep-alive");
        context.Response.Headers.Append("X-Accel-Buffering", "no");

        return SendEvents(context, cancellationToken);
    }

    private async IAsyncEnumerable<string> SendEvents(HttpContext context, System.Threading.CancellationToken cancellationToken)
    {
        var messageId = 0;
        var eventQueue = Channel.CreateUnbounded<string>();

        // Subscribe to process spawn events
        EventHandler<ProcessSpawnEventArgs> handler = (sender, args) =>
        {
            try
            {
                var sse = FormatServerSentEvent(++messageId, args);
                eventQueue.Writer.TryWrite(sse);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error queuing event: {ex.Message}");
            }
        };

        _scanner.ProcessSpawned += handler;

        try
        {
            while (await eventQueue.Reader.WaitToReadAsync(cancellationToken))
            {
                if (eventQueue.Reader.TryRead(out var sse))
                {
                    yield return sse;
                }
            }
        }
        finally
        {
            _scanner.ProcessSpawned -= handler;
            eventQueue.Writer.Complete();
        }
    }

    private static string FormatServerSentEvent(int messageId, ProcessSpawnEventArgs args)
    {
        var data = new ProcessSpawnEvent(
            messageId,
            args.ProcessId,
            args.ProcessName ?? "Unknown",
            args.CommandLine,
            args.ExecutablePath,
            args.Timestamp
        );

        var json = System.Text.Json.JsonSerializer.Serialize(data, AppJsonSerializerContext.Default.ProcessSpawnEvent);
        return $"id: {messageId}\ndata: {json}\n\n";
    }
}

public record ServerEvent(int Id, string Message, DateTime Timestamp);

public record ProcessSpawnEvent(
    int MessageId,
    int ProcessId,
    string ProcessName,
    string? CommandLine,
    string? ExecutablePath,
    DateTime Timestamp
);
