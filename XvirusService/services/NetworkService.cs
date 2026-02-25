using System.Diagnostics;
using System.Text.RegularExpressions;
using Xvirus;
using XvirusService.Model;

namespace XvirusService.Services;

public class NetworkService
{
    private readonly Scanner _scanner;

    public NetworkService(Scanner scanner)
    {
        _scanner = scanner;
    }

    public async Task<List<NetworkConnectionDTO>> GetConnectionsAsync()
    {
        var output = await RunNetstatAsync();
        var rawEntries = ParseNetstat(output);

        // Resolve each unique PID to a file path once
        var pidToPath = new Dictionary<int, string>();
        foreach (var entry in rawEntries)
        {
            if (entry.Pid > 0 && !pidToPath.ContainsKey(entry.Pid))
                pidToPath[entry.Pid] = ResolveProcessPath(entry.Pid);
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

        var connections = new List<NetworkConnectionDTO>(rawEntries.Count);
        foreach (var entry in rawEntries)
        {
            var filePath = entry.Pid > 0 && pidToPath.TryGetValue(entry.Pid, out var p) ? p : string.Empty;
            var score = !string.IsNullOrEmpty(filePath) && pathToScore.TryGetValue(filePath, out var s) ? s : 0.0;

            connections.Add(new NetworkConnectionDTO
            {
                Protocol = entry.Protocol,
                LocalAddress = entry.LocalAddress,
                RemoteAddress = entry.RemoteAddress,
                State = entry.State,
                Pid = entry.Pid,
                FileName = string.IsNullOrEmpty(filePath) ? string.Empty : Path.GetFileName(filePath),
                FilePath = filePath,
                Score = score,
            });
        }

        return connections;
    }

    private static async Task<string> RunNetstatAsync()
    {
        var psi = new ProcessStartInfo("netstat", "-ano")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var proc = Process.Start(psi);
        if (proc == null) return string.Empty;
        var output = await proc.StandardOutput.ReadToEndAsync();
        await proc.WaitForExitAsync();
        return output;
    }

    private record RawEntry(string Protocol, string LocalAddress, string RemoteAddress, string State, int Pid);

    private static List<RawEntry> ParseNetstat(string output)
    {
        var entries = new List<RawEntry>();
        // Matches both TCP (has State column) and UDP (no State column) lines
        var lineRe = new Regex(
            @"^\s*(TCP|UDP)\s+(\S+)\s+(\S+)\s+(?:(\S+)\s+)?(\d+)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Multiline);

        foreach (Match m in lineRe.Matches(output))
        {
            entries.Add(new RawEntry(
                m.Groups[1].Value.ToUpper(),
                m.Groups[2].Value,
                m.Groups[3].Value,
                m.Groups[4].Value,
                int.TryParse(m.Groups[5].Value, out var pid) ? pid : 0
            ));
        }

        return entries;
    }

    private static string ResolveProcessPath(int pid)
    {
        try
        {
            using var proc = Process.GetProcessById(pid);
            return proc.MainModule?.FileName ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}
