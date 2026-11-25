using System.Collections.Concurrent;
using System.Runtime.Versioning;
using Xvirus;
using XvirusService.Model;

namespace XvirusService.Services;

/// <summary>
/// Lists current TCP/UDP connections with per-process scan scores and resolved
/// remote domain names. Backed by the IPHelper API (<see cref="IpHelper"/>),
/// <see cref="EtwDnsListener"/>, and a file-mtime-keyed scan score cache so
/// UI refreshes stay fast.
/// </summary>
[SupportedOSPlatform("windows")]
public class NetworkService
{
    private readonly Scanner _scanner;
    private readonly EtwDnsListener _dns;
    private readonly ConcurrentDictionary<string, (long LastWriteTicks, double Score)> _scoreCache =
        new(StringComparer.OrdinalIgnoreCase);

    public NetworkService(Scanner scanner, EtwDnsListener dns, Rules rules)
    {
        _scanner = scanner;
        _dns = dns;
        rules.RulesChanged += _ => InvalidateCache();
    }

    public void InvalidateCache() => _scoreCache.Clear();

    public async Task<List<NetworkConnectionDTO>> GetConnectionsAsync()
    {
        List<NetworkEndpoint> endpoints;
        try { endpoints = IpHelper.GetEndpoints(); }
        catch (Exception ex)
        {
            Console.WriteLine($"NetworkService: IPHelper snapshot failed – {ex.Message}");
            return new List<NetworkConnectionDTO>();
        }

        // Resolve each unique PID to a file path once using QueryFullProcessImageName
        // (more reliable than Process.MainModule across 32/64-bit & protected processes).
        var pidToPath = new Dictionary<int, string>();
        foreach (var ep in endpoints)
        {
            if (ep.Pid > 0 && !pidToPath.ContainsKey(ep.Pid))
                pidToPath[ep.Pid] = ProcessControl.ResolveProcessPath(ep.Pid) ?? string.Empty;
        }

        // Resolve/cache scan score for each unique file path once.
        var pathToScore = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var (_, path) in pidToPath)
        {
            if (!string.IsNullOrEmpty(path) && !pathToScore.ContainsKey(path))
                pathToScore[path] = await GetCachedScoreAsync(path);
        }

        var connections = new List<NetworkConnectionDTO>(endpoints.Count);
        foreach (var ep in endpoints)
        {
            var filePath = ep.Pid > 0 && pidToPath.TryGetValue(ep.Pid, out var p) ? p : string.Empty;
            var score = !string.IsNullOrEmpty(filePath) && pathToScore.TryGetValue(filePath, out var s) ? s : 0.0;
            var remoteDomain = string.IsNullOrEmpty(ep.RemoteAddress) || ep.RemoteAddress == "*"
                ? string.Empty
                : _dns.TryGetDomain(ep.RemoteAddress);

            connections.Add(new NetworkConnectionDTO
            {
                Protocol = ep.Protocol,
                LocalAddress = FormatEndpoint(ep.LocalAddress, ep.LocalPort),
                RemoteAddress = FormatEndpoint(ep.RemoteAddress, ep.RemotePort),
                RemoteDomain = remoteDomain,
                State = ep.State,
                Pid = ep.Pid,
                FileName = string.IsNullOrEmpty(filePath) ? string.Empty : Path.GetFileName(filePath),
                FilePath = filePath,
                Score = score,
            });
        }

        return connections;
    }

    private async Task<double> GetCachedScoreAsync(string path)
    {
        long mtime = 0;
        try { mtime = File.GetLastWriteTimeUtc(path).Ticks; }
        catch { /* best effort */ }

        if (_scoreCache.TryGetValue(path, out var cached) && cached.LastWriteTicks == mtime)
            return cached.Score;

        var result = await Task.Run(() => _scanner.ScanFile(path));
        double score = result.MalwareScore < 0 ? 0.0 : result.MalwareScore;
        _scoreCache[path] = (mtime, score);
        return score;
    }

    private static string FormatEndpoint(string address, int port)
    {
        if (string.IsNullOrEmpty(address) || address == "*")
            return "*";
        return $"{address}:{port}";
    }
}
