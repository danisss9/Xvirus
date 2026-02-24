using System.Text;

namespace XvirusService.Services;

/// <summary>
/// Singleton that keeps every connected SSE client's <see cref="HttpResponse"/>
/// and lets any other service push named events to all of them.
/// </summary>
public class ServerEventService
{
    private readonly List<HttpResponse> _clients = [];
    private readonly object _lock = new();

    // -----------------------------------------------------------------------
    // SSE endpoint – called by ServerSentEventApi to hold a connection open
    // -----------------------------------------------------------------------

    public async Task HandleEvents(HttpContext context, CancellationToken cancellationToken)
    {
        context.Response.Headers.Append("Content-Type", "text/event-stream");
        context.Response.Headers.Append("Cache-Control", "no-cache");
        context.Response.Headers.Append("Connection", "keep-alive");
        context.Response.Headers.Append("X-Accel-Buffering", "no");
        context.Response.Headers.Append("Access-Control-Allow-Origin", "*");

        await context.Response.Body.FlushAsync(cancellationToken);

        lock (_lock) _clients.Add(context.Response);

        try
        {
            // Hold the connection open until the client disconnects or the host stops.
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        catch (OperationCanceledException) { }
        finally
        {
            lock (_lock) _clients.Remove(context.Response);
        }
    }

    // -----------------------------------------------------------------------
    // Push API – called by other services to notify the front-end
    // -----------------------------------------------------------------------

    /// <summary>
    /// Broadcast a named SSE event with a pre-serialised JSON payload to every
    /// connected front-end client.
    /// </summary>
    /// <param name="eventType">
    ///   Logical event name, e.g. <c>"updating"</c>, <c>"update-complete"</c>,
    ///   <c>"threat"</c>, <c>"protection-changed"</c>.
    /// </param>
    /// <param name="jsonPayload">Already-serialised JSON string used as the SSE <c>data</c> field.</param>
    public async Task SendAsync(string eventType, string jsonPayload)
    {
        List<HttpResponse> snapshot;
        lock (_lock) snapshot = [.. _clients];

        if (snapshot.Count == 0) return;

        // SSE wire format:  "event: <type>\ndata: <json>\n\n"
        var bytes = Encoding.UTF8.GetBytes($"event: {eventType}\ndata: {jsonPayload}\n\n");

        foreach (var response in snapshot)
        {
            try
            {
                await response.Body.WriteAsync(bytes);
                await response.Body.FlushAsync();
            }
            catch
            {
                // Client disconnected; it will be removed from the list
                // when its Task.Delay above is cancelled.
            }
        }
    }
}
