using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using YaraTranslator.Parsing;
using YaraTranslator.Semantics;

namespace YaraTranslator.Emit;

public sealed class Manifest
{
    public int Schema { get; set; } = 1;
    public string SourceHash { get; set; } = string.Empty;
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<ManifestEntry> Entries { get; set; } = new();
}

public sealed class ManifestEntry
{
    public string Rule { get; set; } = string.Empty;
    public string Anchor { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public List<string> Dependencies { get; set; } = new();
    public string Target { get; set; } = string.Empty;

    [JsonIgnore]
    public string CanonicalKey => $"{Anchor}|{Name}|{string.Join("&", Dependencies)}";
}

public static class ManifestStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static Manifest? Read(string path)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            return JsonSerializer.Deserialize<Manifest>(File.ReadAllText(path), JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public static Manifest Build(TranslationResult result, string sourceHash, ISet<string>? acceptedKeys = null)
    {
        var manifest = new Manifest
        {
            SourceHash = sourceHash,
            GeneratedAt = DateTimeOffset.UtcNow
        };

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in result.Lines
                     .Where(l => acceptedKeys is null || acceptedKeys.Contains(l.Key))
                     .OrderBy(l => l.Name, StringComparer.Ordinal)
                     .ThenBy(l => l.AnchorHex, StringComparer.Ordinal)
                     .ThenBy(l => string.Join("&", l.Dependencies.Select(d => d.ToDbText())), StringComparer.Ordinal))
        {
            var entry = new ManifestEntry
            {
                Rule = line.RuleName,
                Anchor = line.AnchorHex,
                Name = line.Name,
                Dependencies = line.Dependencies.Select(d => d.ToDbText()).ToList(),
                Target = line.Target == TargetKind.Pe ? "pe" : "script"
            };

            if (seen.Add(entry.Target + "|" + entry.CanonicalKey))
                manifest.Entries.Add(entry);
        }

        return manifest;
    }

    public static void Write(Manifest manifest, string path)
    {
        var text = JsonSerializer.Serialize(manifest, JsonOptions);
        WriteAtomic(path, text + Environment.NewLine);
    }

    public static string ComputeSourceHash(IEnumerable<string> files)
    {
        using var sha = SHA256.Create();
        using var stream = new MemoryStream();

        foreach (var file in files.OrderBy(f => f, StringComparer.Ordinal))
        {
            if (!File.Exists(file))
                continue;

            var bytes = Encoding.UTF8.GetBytes(file + "\0");
            stream.Write(bytes, 0, bytes.Length);
            var content = File.ReadAllBytes(file);
            stream.Write(content, 0, content.Length);
        }

        stream.Position = 0;
        return Convert.ToHexString(sha.ComputeHash(stream));
    }

    private static void WriteAtomic(string path, string text)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var temp = path + ".tmp." + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temp, text, new UTF8Encoding(false));
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }
}

public sealed class TranslationReport
{
    public string ToolVersion { get; set; } = "0.1.0";
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
    public string SourceHash { get; set; } = string.Empty;
    public string EngineProfile { get; set; } = "strict";
    public string Target { get; set; } = "auto";
    public string[] SourceFiles { get; set; } = Array.Empty<string>();
    public TranslationSummary Summary { get; set; } = new();
    public List<ReportRule> Rules { get; set; } = new();
    public List<ReportDiagnostic> Diagnostics { get; set; } = new();
}

public sealed class ReportRule
{
    public string Name { get; set; } = string.Empty;
    public string File { get; set; } = string.Empty;
    public int Line { get; set; }
    public string Status { get; set; } = string.Empty;
    public string[] Targets { get; set; } = Array.Empty<string>();
    public int Lines { get; set; }
    public string[] Features { get; set; } = Array.Empty<string>();
    public List<ReportDiagnostic> Diagnostics { get; set; } = new();
}

public sealed class ReportDiagnostic
{
    public string Code { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? File { get; set; }
    public int? Line { get; set; }
    public int? Column { get; set; }
}

public static class ReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public static TranslationReport Build(
        TranslationResult result,
        TranslationOptions options,
        IReadOnlyList<string> sourceFiles,
        string sourceHash)
    {
        var report = new TranslationReport
        {
            GeneratedAt = DateTimeOffset.UtcNow,
            SourceHash = sourceHash,
            EngineProfile = options.Engine == EngineProfile.Strict ? "strict" : "legacy",
            Target = options.Target.ToString().ToLowerInvariant(),
            SourceFiles = sourceFiles.ToArray(),
            Summary = result.Summary,
            Diagnostics = result.Diagnostics.Select(ToReportDiagnostic).ToList()
        };

        foreach (var rule in result.Rules.OrderBy(r => r.Rule.Name, StringComparer.Ordinal))
        {
            report.Rules.Add(new ReportRule
            {
                Name = rule.Rule.Name,
                File = rule.Rule.File,
                Line = rule.Rule.Line,
                Status = rule.Status.ToString().ToLowerInvariant(),
                Targets = rule.Lines.Select(l => l.Target == TargetKind.Pe ? "pe" : "script").Distinct().ToArray(),
                Lines = rule.Lines.Count,
                Features = rule.Features.OrderBy(f => f, StringComparer.Ordinal).ToArray(),
                Diagnostics = rule.Diagnostics.Select(ToReportDiagnostic).ToList()
            });
        }

        return report;
    }

    public static void Write(TranslationReport report, string path)
    {
        var text = JsonSerializer.Serialize(report, JsonOptions);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var temp = path + ".tmp." + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temp, text + Environment.NewLine, new UTF8Encoding(false));
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }

    public static ReportDiagnostic ToReportDiagnostic(Diagnostic diagnostic) => new()
    {
        Code = diagnostic.Code,
        Severity = diagnostic.Severity.ToString().ToLowerInvariant(),
        Message = diagnostic.Message,
        File = diagnostic.Span?.File,
        Line = diagnostic.Span?.Line,
        Column = diagnostic.Span?.Column
    };
}
