using YaraTranslator.Emit;
using YaraTranslator.Parsing;

namespace YaraTranslator.Validation;

public sealed class DbValidationResult
{
    public string Path { get; init; } = string.Empty;
    public int Lines { get; set; }
    public int Entries { get; set; }
    public List<Diagnostic> Diagnostics { get; } = new();
    public bool HasErrors => Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
}

public static class HeuristicDbValidator
{
    public static DbValidationResult Validate(string path)
    {
        var result = new DbValidationResult { Path = path };

        if (!File.Exists(path))
        {
            result.Diagnostics.Add(new Diagnostic("YARA5101", DiagnosticSeverity.Error, $"Database file not found: {path}"));
            return result;
        }

        var parsed = HeuristicDbParser.Parse(path);
        result.Diagnostics.AddRange(parsed.Diagnostics);
        result.Entries = parsed.Entries.Count;
        result.Lines = parsed.Entries.Count;

        if (parsed.Entries.Count == 0)
        {
            result.Diagnostics.Add(new Diagnostic("YARA5102", DiagnosticSeverity.Error, $"Database contains no usable entries: {path}"));
        }

        foreach (var entry in parsed.Entries)
        {
            if (!HeuristicDbParser.IsValidHex(entry.AnchorHex, out var anchorError))
            {
                result.Diagnostics.Add(new Diagnostic(
                    "YARA5108",
                    DiagnosticSeverity.Error,
                    $"Entry '{entry.Name}' has an invalid anchor: {anchorError}"));
            }

            var originalAnchor = entry.OriginalLine.Split(',')[0];
            if (originalAnchor.Any(c => c is >= 'a' and <= 'f'))
            {
                result.Diagnostics.Add(new Diagnostic(
                    "YARA5109",
                    DiagnosticSeverity.Error,
                    $"Entry '{entry.Name}' uses lowercase hex in its anchor; the scanner produces uppercase hex."));
            }

            var anchorBytes = entry.AnchorHex.Length / 2;
            if (anchorBytes < 4)
            {
                result.Diagnostics.Add(new Diagnostic(
                    "YARA5103",
                    DiagnosticSeverity.Warning,
                    $"Anchor '{entry.AnchorHex}' is only {anchorBytes} byte(s); short patterns cause false positives."));
            }

            var positives = new HashSet<string>(StringComparer.Ordinal);
            var negatives = new HashSet<string>(StringComparer.Ordinal);

            foreach (var dep in entry.Dependencies)
            {
                var bytes = dep.PatternHex.Length / 2;
                if (bytes < 4)
                {
                    result.Diagnostics.Add(new Diagnostic(
                        "YARA5104",
                        DiagnosticSeverity.Warning,
                        $"Dependency '{dep.PatternHex}' is only {bytes} byte(s); short dependencies increase the false-positive risk."));
                }

                if (dep.PatternHex.Any(c => c is >= 'a' and <= 'f'))
                {
                    result.Diagnostics.Add(new Diagnostic(
                        "YARA5105",
                        DiagnosticSeverity.Error,
                        $"Dependency '{dep.PatternHex}' uses lowercase hex; the scanner produces uppercase hex."));
                }

                if (dep.Negated)
                    negatives.Add(dep.PatternHex);
                else
                    positives.Add(dep.PatternHex);
            }

            if (positives.Overlaps(negatives))
            {
                result.Diagnostics.Add(new Diagnostic(
                    "YARA5106",
                    DiagnosticSeverity.Error,
                    $"Entry '{entry.Name}' requires the same dependency to be present and absent."));
            }

            if (negatives.Contains(entry.AnchorHex))
            {
                result.Diagnostics.Add(new Diagnostic(
                    "YARA5107",
                    DiagnosticSeverity.Error,
                    $"Entry '{entry.Name}' negates its own anchor, so it can never match."));
            }
        }

        foreach (var group in parsed.Entries.GroupBy(e => e.AnchorHex, StringComparer.Ordinal))
        {
            if (group.Count() <= 1)
                continue;

            var first = group.First();
            var same = group.All(e =>
                e.Name == first.Name &&
                HeuristicDbParser.DependenciesEqual(e.Dependencies, first.Dependencies));

            result.Diagnostics.Add(new Diagnostic(
                "YARA5110",
                same ? DiagnosticSeverity.Warning : DiagnosticSeverity.Error,
                same
                    ? $"Anchor '{group.Key}' is duplicated with the same name and dependencies."
                    : $"Anchor '{group.Key}' is duplicated with different names or dependencies; DB.LoadDictionary keeps only the first line."));
        }

        return result;
    }

    public static IReadOnlyList<DbValidationResult> ValidateAll(IEnumerable<string> paths)
    {
        var results = new List<DbValidationResult>();
        foreach (var path in paths)
        {
            if (Directory.Exists(path))
            {
                foreach (var file in Directory.EnumerateFiles(path, "*.db", SearchOption.TopDirectoryOnly)
                             .OrderBy(f => f, StringComparer.Ordinal))
                {
                    results.Add(Validate(file));
                }
            }
            else
            {
                results.Add(Validate(path));
            }
        }

        return results;
    }
}
