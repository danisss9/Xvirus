// Xvirus C# SDK benchmark host.
// Usage: CSharpHost <baseFolder> <planFile> <outFile>
// Plan lines: baseline | load | unload | update | scan <warm> <reps> <path> | folder <path> | pfolder <threads> <path>
using System.Diagnostics;
using System.Text.Json;
using Xvirus;

var baseFolder = args[0];
var plan = File.ReadAllLines(args[1]);
using var output = new StreamWriter(args[2]) { AutoFlush = true };

XvirusSDK.BaseFolder(baseFolder);
var proc = Process.GetCurrentProcess();

(double cpuMs, double wsMb, double peakMb, double privMb) Snapshot()
{
    proc.Refresh();
    return (proc.TotalProcessorTime.TotalMilliseconds, proc.WorkingSet64 / 1048576.0,
            proc.PeakWorkingSet64 / 1048576.0, proc.PrivateMemorySize64 / 1048576.0);
}

void Emit(string op, string path, List<double> times, double cpuMs, string result, int files = 0, int threads = 1)
{
    var s = Snapshot();
    var obj = new Dictionary<string, object?>
    {
        ["op"] = op, ["path"] = path, ["times_ms"] = times, ["cpu_ms"] = cpuMs,
        ["ws_mb"] = s.wsMb, ["peak_ws_mb"] = s.peakMb, ["private_mb"] = s.privMb,
        ["result"] = result, ["files"] = files, ["threads"] = threads
    };
    output.WriteLine(JsonSerializer.Serialize(obj));
}

foreach (var raw in plan)
{
    var line = raw.Trim();
    if (line.Length == 0 || line.StartsWith('#')) continue;
    var parts = line.Split(' ', 2);
    var op = parts[0];
    var sw = new Stopwatch();
    var before = Snapshot();

    switch (op)
    {
        case "baseline":
            Emit(op, "", new(), 0, "");
            break;
        case "load":
            sw.Start(); XvirusSDK.Load(); sw.Stop();
            Emit(op, "", new() { sw.Elapsed.TotalMilliseconds }, Snapshot().cpuMs - before.cpuMs, "ok");
            break;
        case "unload":
            sw.Start(); XvirusSDK.Unload(); sw.Stop();
            Emit(op, "", new() { sw.Elapsed.TotalMilliseconds }, Snapshot().cpuMs - before.cpuMs, "ok");
            break;
        case "update":
        {
            sw.Start();
            string res;
            try { res = XvirusSDK.CheckUpdates(); } catch (Exception e) { res = "ERROR: " + e.Message; }
            sw.Stop();
            Emit(op, "", new() { sw.Elapsed.TotalMilliseconds }, Snapshot().cpuMs - before.cpuMs, res);
            break;
        }
        case "scan":
        {
            var p = parts[1].Split(' ', 3);
            int warm = int.Parse(p[0]), reps = int.Parse(p[1]);
            var path = p[2];
            var times = new List<double>();
            string name = "";
            // First (cold) scan of this file is recorded separately as warm-up.
            for (int i = 0; i < warm; i++)
            {
                sw.Restart(); name = XvirusSDK.Scan(path).Name; sw.Stop();
                times.Add(sw.Elapsed.TotalMilliseconds);
            }
            var cpu0 = Snapshot().cpuMs;
            var timed = new List<double>();
            for (int i = 0; i < reps; i++)
            {
                sw.Restart(); name = XvirusSDK.Scan(path).Name; sw.Stop();
                timed.Add(sw.Elapsed.TotalMilliseconds);
            }
            var cpu = Snapshot().cpuMs - cpu0;
            var o = new Dictionary<string, object?>();
            Emit(op, path, timed, cpu, name + "|warm=" + string.Join(",", times.Select(t => t.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture))));
            break;
        }
        case "folder":
        {
            var path = parts[1];
            sw.Start();
            var results = XvirusSDK.ScanFolder(path).ToList();
            sw.Stop();
            Emit(op, path, new() { sw.Elapsed.TotalMilliseconds }, Snapshot().cpuMs - before.cpuMs,
                 $"malware={results.Count(r => r.IsMalware)}", results.Count);
            break;
        }
        case "pfolder":
        {
            var p = parts[1].Split(' ', 2);
            int threads = int.Parse(p[0]);
            var path = p[1];
            var files = Directory.GetFiles(path, "*", SearchOption.AllDirectories);
            int malware = 0;
            sw.Start();
            Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = threads }, f =>
            {
                if (XvirusSDK.Scan(f).IsMalware) Interlocked.Increment(ref malware);
            });
            sw.Stop();
            Emit(op, path, new() { sw.Elapsed.TotalMilliseconds }, Snapshot().cpuMs - before.cpuMs,
                 $"malware={malware}", files.Length, threads);
            break;
        }
    }
}
