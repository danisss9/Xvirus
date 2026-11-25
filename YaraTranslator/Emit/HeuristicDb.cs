using System.Text;
using YaraTranslator.Parsing;
using YaraTranslator.Semantics;

namespace YaraTranslator.Emit;

public sealed class DbEntry
{
    public required string AnchorHex { get; init; }
    public required string Name { get; init; }
    public IReadOnlyList<Dependency> Dependencies { get; init; } = Array.Empty<Dependency>();
    public required string OriginalLine { get; init; }
    public bool PreserveOriginal { get; init; }

    public string CanonicalKey => $"{AnchorHex}|{Name}|{string.Join("&", Dependencies.Select(d => d.ToDbText()))}";

    public string ToLine()
    {
        if (PreserveOriginal)
            return OriginalLine;

        var deps = Dependencies.Count == 0
            ? string.Empty
            : "," + string.Join("&", Dependencies.Select(d => d.ToDbText()));
        return $"{AnchorHex},{Name}{deps}";
    }
}

public sealed class ParsedDb
{
    public List<DbEntry> Entries { get; } = new();
    public List<Diagnostic> Diagnostics { get; } = new();
    public bool HasErrors => Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
}

public static class HeuristicDbParser
{
    public static ParsedDb Parse(string path)
    {
        var result = new ParsedDb();
        var lines = File.ReadAllLines(path);
        if (lines.Length == 0)
        {
            result.Diagnostics.Add(new Diagnostic("YARA5001", DiagnosticSeverity.Error, $"Database is empty: {path}"));
            return result;
        }

        var seen = new Dictionary<string, DbEntry>(StringComparer.Ordinal);

        for (var i = 0; i < lines.Length; i++)
        {
            var raw = lines[i];
            if (string.IsNullOrWhiteSpace(raw))
            {
                result.Diagnostics.Add(new Diagnostic(
                    "YARA5002",
                    DiagnosticSeverity.Error,
                    $"Blank line at {path}:{i + 1} is not valid in a heuristic database."));
                continue;
            }

            var parts = raw.Split(',');
            if (parts.Length is < 2 or > 3)
            {
                result.Diagnostics.Add(new Diagnostic(
                    "YARA5003",
                    DiagnosticSeverity.Error,
                    $"Line {i + 1} has {parts.Length} fields; expected 2 or 3."));
                continue;
            }

            var anchorRaw = parts[0].Trim().Replace("-", string.Empty, StringComparison.Ordinal);
            if (anchorRaw.Length == 0)
            {
                result.Diagnostics.Add(new Diagnostic(
                    "YARA5004",
                    DiagnosticSeverity.Error,
                    $"Line {i + 1} has an empty anchor."));
                continue;
            }

            if (!IsValidHex(anchorRaw, out var anchorError))
            {
                // Existing databases may contain malformed or odd-length anchors. They do
                // not prevent DB.Load from working and must be preserved byte-for-byte
                // during a merge.
                result.Diagnostics.Add(new Diagnostic(
                    "YARA5004",
                    DiagnosticSeverity.Warning,
                    $"Line {i + 1} has an invalid anchor: {anchorError}"));
            }

            var name = parts[1];
            if (string.IsNullOrWhiteSpace(name))
            {
                result.Diagnostics.Add(new Diagnostic(
                    "YARA5005",
                    DiagnosticSeverity.Error,
                    $"Line {i + 1} has an empty detection name."));
                continue;
            }

            var deps = new List<Dependency>();
            if (parts.Length == 3)
            {
                var seenDeps = new HashSet<string>(StringComparer.Ordinal);
                foreach (var rawDep in parts[2].Split('&'))
                {
                    var dep = rawDep.Trim();
                    if (dep.Length == 0)
                    {
                        result.Diagnostics.Add(new Diagnostic(
                            "YARA5006",
                            DiagnosticSeverity.Error,
                            $"Line {i + 1} contains an empty dependency."));
                        continue;
                    }

                    var negated = dep[0] == '!';
                    var hex = negated ? dep[1..] : dep;
                    hex = hex.Replace("-", string.Empty, StringComparison.Ordinal);

                    if (!IsValidHex(hex, out var depError))
                    {
                        result.Diagnostics.Add(new Diagnostic(
                            "YARA5007",
                            DiagnosticSeverity.Warning,
                            $"Line {i + 1} has an invalid dependency '{rawDep}': {depError}"));
                        continue;
                    }

                    hex = hex.ToUpperInvariant();

                    if (dep.Any(c => c is >= 'a' and <= 'f'))
                    {
                        result.Diagnostics.Add(new Diagnostic(
                            "YARA5009",
                            DiagnosticSeverity.Warning,
                            $"Line {i + 1} dependency '{rawDep}' uses lowercase hex; the scanner produces uppercase hex."));
                    }

                    var key = (negated ? "!" : string.Empty) + hex;
                    if (seenDeps.Add(key))
                        deps.Add(new Dependency(hex, negated));
                    else
                        result.Diagnostics.Add(new Diagnostic(
                            "YARA5008",
                            DiagnosticSeverity.Warning,
                            $"Line {i + 1} repeats dependency '{rawDep}'."));
                }
            }

            if (parts[0].Any(c => c is >= 'a' and <= 'f'))
            {
                // Lowercase hex is accepted by the parser for robustness, but it will not
                // match the uppercase hex stream produced by the Aho matcher.
                result.Diagnostics.Add(new Diagnostic(
                    "YARA5009",
                    DiagnosticSeverity.Warning,
                    $"Line {i + 1} uses lowercase hex; the scanner emits uppercase hex, so this pattern will not match."));
            }

            var entry = new DbEntry
            {
                AnchorHex = anchorRaw.ToUpperInvariant(),
                Name = name,
                Dependencies = deps,
                OriginalLine = raw,
                PreserveOriginal = true
            };

            if (seen.TryGetValue(entry.AnchorHex, out var existing))
            {
                var same = existing.Name == entry.Name &&
                           DependenciesEqual(existing.Dependencies, entry.Dependencies);

                result.Diagnostics.Add(new Diagnostic(
                    "YARA5010",
                    DiagnosticSeverity.Warning,
                    same
                        ? $"Line {i + 1} duplicates anchor '{entry.AnchorHex}' with the same name and dependencies."
                        : $"Line {i + 1} reuses anchor '{entry.AnchorHex}' with a different name or dependency list; DB.LoadDictionary keeps only the first line."));
            }
            else
            {
                seen.Add(entry.AnchorHex, entry);
            }

            result.Entries.Add(entry);
        }

        return result;
    }

    public static bool DependenciesEqual(IReadOnlyList<Dependency> left, IReadOnlyList<Dependency> right)
    {
        if (left.Count != right.Count)
            return false;

        return left.All(l => right.Any(r =>
            l.PatternHex == r.PatternHex && l.Negated == r.Negated));
    }

    public static bool IsValidHex(string hex, out string error)
    {
        if (hex.Length == 0)
        {
            error = "hex string is empty";
            return false;
        }

        if (hex.Length % 2 != 0)
        {
            error = "hex string has an odd number of characters";
            return false;
        }

        if (hex.Any(c => !Uri.IsHexDigit(c)))
        {
            error = "hex string contains a non-hex character";
            return false;
        }

        error = string.Empty;
        return true;
    }
}

public sealed class DbWriteResult
{
    public string? Path { get; set; }
    public int Lines { get; set; }
    public int Added { get; set; }
    public int Replaced { get; set; }
    public int Skipped { get; set; }
    public int Pruned { get; set; }
    public bool WroteFile { get; set; }
    public HashSet<string> AcceptedKeys { get; } = new(StringComparer.Ordinal);
}

public sealed class DbMergeException : Exception
{
    public DbMergeException(string message) : base(message)
    {
    }
}

public sealed class HeuristicDbWriter
{
    public DbWriteResult Write(
        IReadOnlyList<SignatureLine> lines,
        string outputPath,
        string? mergePath,
        CollisionPolicy policy,
        ISet<string>? previouslyOwnedKeys = null)
    {
        var result = new DbWriteResult { Path = outputPath };
        var existing = new List<DbEntry>();

        if (!string.IsNullOrWhiteSpace(mergePath) &&
            File.Exists(mergePath) &&
            new FileInfo(mergePath).Length > 0)
        {
            var parsed = HeuristicDbParser.Parse(mergePath);
            if (parsed.HasErrors)
            {
                throw new DbMergeException(
                    $"Existing database '{mergePath}' is invalid: {string.Join("; ", parsed.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Message))}");
            }

            existing.AddRange(parsed.Entries);
        }

        var currentCanonicalKeys = new HashSet<string>(StringComparer.Ordinal);
        var newLines = new List<SignatureLine>();
        var seenNewKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var line in lines
                     .OrderBy(l => l.Name, StringComparer.Ordinal)
                     .ThenBy(l => l.AnchorHex, StringComparer.Ordinal)
                     .ThenBy(l => string.Join("&", l.Dependencies.Select(d => d.ToDbText())), StringComparer.Ordinal))
        {
            var key = line.Key;
            if (seenNewKeys.Add(key))
                newLines.Add(line);

            currentCanonicalKeys.Add(
                $"{line.AnchorHex}|{line.Name}|{string.Join("&", line.Dependencies.Select(d => d.ToDbText()))}");
        }

        var byAnchor = new Dictionary<string, int>(StringComparer.Ordinal);
        var duplicateAnchors = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < existing.Count; i++)
        {
            if (!byAnchor.TryAdd(existing[i].AnchorHex, i))
                duplicateAnchors.Add(existing[i].AnchorHex);
        }

        if (previouslyOwnedKeys is not null)
        {
            for (var i = existing.Count - 1; i >= 0; i--)
            {
                var entry = existing[i];
                if (!previouslyOwnedKeys.Contains(entry.CanonicalKey))
                    continue;
                if (currentCanonicalKeys.Contains(entry.CanonicalKey))
                    continue;

                existing.RemoveAt(i);
                result.Pruned++;
            }

            byAnchor.Clear();
            duplicateAnchors.Clear();
            for (var i = 0; i < existing.Count; i++)
            {
                if (!byAnchor.TryAdd(existing[i].AnchorHex, i))
                    duplicateAnchors.Add(existing[i].AnchorHex);
            }
        }

        foreach (var line in newLines)
        {
            var deps = line.Dependencies.ToList();
            var entry = new DbEntry
            {
                AnchorHex = line.AnchorHex,
                Name = line.Name,
                Dependencies = deps,
                OriginalLine = line.ToDbLine()
            };

            if (byAnchor.TryGetValue(entry.AnchorHex, out var index))
            {
                if (duplicateAnchors.Contains(entry.AnchorHex))
                {
                    throw new DbMergeException(
                        $"The existing database already contains duplicate anchor '{entry.AnchorHex}'. Clean up the duplicate entries before merging this rule.");
                }

                var current = existing[index];
                var same = current.Name == entry.Name &&
                           HeuristicDbParser.DependenciesEqual(current.Dependencies, entry.Dependencies);

                if (same)
                {
                    result.Skipped++;
                    result.AcceptedKeys.Add(line.Key);
                    continue;
                }

                switch (policy)
                {
                    case CollisionPolicy.Error:
                        throw new DbMergeException(
                            $"Anchor '{entry.AnchorHex}' already exists as '{current.Name}' with different dependencies. Use --collision skip, keep or overwrite to resolve it.");
                    case CollisionPolicy.Skip:
                    case CollisionPolicy.Keep:
                        result.Skipped++;
                        continue;
                    case CollisionPolicy.Overwrite:
                        existing[index] = entry;
                        result.Replaced++;
                        result.AcceptedKeys.Add(line.Key);
                        continue;
                }
            }

            existing.Add(entry);
            byAnchor[entry.AnchorHex] = existing.Count - 1;
            result.Added++;
            result.AcceptedKeys.Add(line.Key);
        }

        result.Lines = existing.Count;

        if (existing.Count == 0)
        {
            result.WroteFile = false;
            return result;
        }

        var text = string.Join("\r\n", existing.Select(e => e.ToLine())) + "\r\n";
        WriteAtomic(outputPath, text);
        result.WroteFile = true;
        return result;
    }

    private static void WriteAtomic(string path, string text)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var temp = path + ".tmp." + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temp, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }
}
