using System.Runtime.Versioning;
using Xvirus;
using XvirusService.Model;

namespace XvirusService.Services;

/// <summary>
/// Lists current TCP/UDP connections with per-process scan scores. Backed by
/// the IPHelper API (<see cref="IpHelper"/>) instead of spawning
/// <c>netstat -ano</c>, so a UI refresh is a sub-millisecond in-process call.
/// </summary>
[SupportedOSPlatform("windows")]
public class NetworkService
{
    private readonly Scanner _scanner;

    public NetworkService(Scanner scanner)
    {
        _scanner = scanner;
    }

    public async Task<List<NetworkConnectionDTO>> GetConnectionsAsync()
    {
        // Single in-process snapshot — no netstat spawn, no regex parsing.
        List<NetworkEndpoint> endpoints;
        try { endpoints = IpHelper.GetEndpoints(); }
        catch (Exception ex)
        {
            Console.WriteLine($"NetworkService: IPHelper snapshot failed – {ex.Message}");
            return new List<NetworkConnectionDTO>();
        }

        // Resolve each unique PID to a file path once
        var pidToPath = new Dictionary<int, string>();
        foreach (var ep in endpoints)
        {
            if (ep.Pid > 0 && !pidToPath.ContainsKey(ep.Pid))
                pidToPath[ep.Pid] = ResolveProcessPath(ep.Pid);
        }

        // Scan each unique file path once
        var pathToScore = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var (_, path) in pidToPath)
        {
            if (!string.IsNullOrEmpty(path) && !pathToScore.ContainsKey(path))
            {
                var result = await Task.Run(() => _scanner.ScanFile(path));
                pathToScore[path] = result.MalwareScore < 0 ? 0.0 : result.MalwareScore;
            }
        }

        var connections = new List<NetworkConnectionDTO>(endpoints.Count);
        foreach (var ep in endpoints)
        {
            var filePath = ep.Pid > 0 && pidToPath.TryGetValue(ep.Pid, out var p) ? p : string.Empty;
            var score = !string.IsNullOrEmpty(filePath) && pathToScore.TryGetValue(filePath, out var s) ? s : 0.0;

            connections.Add(new NetworkConnectionDTO
            {
                Protocol = ep.Protocol,
                LocalAddress = FormatEndpoint(ep.LocalAddress, ep.LocalPort),
                RemoteAddress = FormatEndpoint(ep.RemoteAddress, ep.RemotePort),
                State = ep.State,
                Pid = ep.Pid,
                FileName = string.IsNullOrEmpty(filePath) ? string.Empty : Path.GetFileName(filePath),
                FilePath = filePath,
                Score = score,
            });
        }

        return connections;
    }

    private static string FormatEndpoint(string address, int port)
    {
        if (string.IsNullOrEmpty(address) || address == "*")
            return "*";
        return $"{address}:{port}";
    }

    private static string ResolveProcessPath(int pid)
    {
        try
        {
            using var proc = System.Diagnostics.Process.GetProcessById(pid);
            return proc.MainModule?.FileName ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}
