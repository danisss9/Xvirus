using YaraTranslator.Parsing;

namespace YaraTranslator.Semantics;

public sealed class LoadedSources
{
    public List<YaraRule> Rules { get; } = new();
    public List<ImportDirective> Imports { get; } = new();
    public List<string> Files { get; } = new();
    public DiagnosticBag Diagnostics { get; } = new();
}

/// <summary>
/// Loads rule files and resolves text-level includes. Includes are restricted to the
/// input roots and explicitly configured include directories so a rule set cannot read
/// arbitrary files from the host.
/// </summary>
public sealed class SourceLoader
{
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private readonly TranslationOptions _options;
    private readonly DiagnosticBag _diagnostics = new();
    private readonly HashSet<string> _visited = new(OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal);
    private readonly List<string> _stack = new();
    private readonly List<string> _roots = new();
    private readonly List<string> _imports = new();

    public SourceLoader(TranslationOptions options)
    {
        _options = options;
    }

    public LoadedSources Load(IEnumerable<string> inputs)
    {
        var result = new LoadedSources();
        var files = new List<string>();

        foreach (var input in inputs)
        {
            if (string.IsNullOrWhiteSpace(input))
                continue;

            var full = Path.GetFullPath(input);
            if (Directory.Exists(full))
            {
                AddRoot(full);
                foreach (var file in EnumerateRuleFiles(full))
                    files.Add(file);
            }
            else if (File.Exists(full))
            {
                AddRoot(Path.GetDirectoryName(full) ?? Directory.GetCurrentDirectory());
                files.Add(full);
            }
            else
            {
                _diagnostics.Error("YARA1201", $"Input path not found: {input}");
            }
        }

        foreach (var dir in _options.IncludeDirectories)
        {
            var full = Path.GetFullPath(dir);
            if (Directory.Exists(full))
                AddRoot(full);
            else
                _diagnostics.Warning("YARA1202", $"Include directory not found: {dir}");
        }

        foreach (var file in files.Distinct(OperatingSystem.IsWindows()
                     ? StringComparer.OrdinalIgnoreCase
                     : StringComparer.Ordinal)
                     .OrderBy(f => f, OperatingSystem.IsWindows()
                         ? StringComparer.OrdinalIgnoreCase
                         : StringComparer.Ordinal))
        {
            LoadFile(Path.GetFullPath(file), 0);
        }

        foreach (var rule in _rules)
            result.Rules.Add(rule);

        foreach (var file in _files)
            result.Files.Add(file);

        foreach (var import in _imports.Distinct(StringComparer.OrdinalIgnoreCase))
            result.Imports.Add(new ImportDirective(import, new SourceSpan(string.Empty, 0, 0)));

        result.Diagnostics.AddRange(_diagnostics.Items);
        return result;
    }

    private readonly List<YaraRule> _rules = new();
    private readonly List<string> _files = new();

    private void LoadFile(string fullPath, int depth)
    {
        if (depth > _options.MaxIncludeDepth)
        {
            _diagnostics.Error("YARA1203", $"Maximum include depth ({_options.MaxIncludeDepth}) exceeded for '{fullPath}'.");
            return;
        }

        if (_stack.Any(p => p.Equals(fullPath, PathComparison)))
        {
            _diagnostics.Error("YARA1204", $"Include cycle detected: {string.Join(" -> ", _stack.Append(fullPath))}");
            return;
        }

        if (!_visited.Add(fullPath))
            return;

        if (!IsAllowed(fullPath))
        {
            _diagnostics.Error("YARA1205", $"Refusing to include file outside the allowed roots: {fullPath}");
            return;
        }

        FileInfo info;
        try
        {
            info = new FileInfo(fullPath);
            if (!info.Exists)
            {
                _diagnostics.Error("YARA1206", $"File not found: {fullPath}");
                return;
            }

            if (info.Length > _options.MaxInputBytes)
            {
                _diagnostics.Error("YARA1207", $"Rule file is larger than {_options.MaxInputBytes} bytes: {fullPath}");
                return;
            }
        }
        catch (Exception ex)
        {
            _diagnostics.Error("YARA1208", $"Could not read '{fullPath}': {ex.Message}");
            return;
        }

        string source;
        try
        {
            source = File.ReadAllText(fullPath);
        }
        catch (Exception ex)
        {
            _diagnostics.Error("YARA1209", $"Could not read '{fullPath}': {ex.Message}");
            return;
        }

        var fileDiagnostics = new DiagnosticBag();
        var file = YaraParser.Parse(source, fullPath, fileDiagnostics);
        _diagnostics.AddRange(fileDiagnostics.Items);
        _files.Add(fullPath);

        _stack.Add(fullPath);
        foreach (var include in file.Includes)
        {
            var resolution = ResolveInclude(fullPath, include.Path);
            if (resolution.Path is null)
            {
                _diagnostics.Error(
                    resolution.OutsideRoot ? "YARA1205" : "YARA1210",
                    resolution.Error ?? $"Could not resolve include '{include.Path}'.",
                    include.Span);
                continue;
            }

            LoadFile(resolution.Path, depth + 1);
        }

        foreach (var import in file.Imports)
            _imports.Add(import.Module);

        _rules.AddRange(file.Rules);
        _stack.RemoveAt(_stack.Count - 1);
    }

    private (string? Path, string? Error, bool OutsideRoot) ResolveInclude(string includingFile, string includePath)
    {
        if (string.IsNullOrWhiteSpace(includePath))
            return (null, "Include path is empty.", false);

        var candidates = new List<string>();
        if (Path.IsPathRooted(includePath))
        {
            candidates.Add(Path.GetFullPath(includePath));
        }
        else
        {
            var baseDir = Path.GetDirectoryName(includingFile) ?? Directory.GetCurrentDirectory();
            candidates.Add(Path.GetFullPath(Path.Combine(baseDir, includePath)));

            foreach (var dir in _options.IncludeDirectories)
                candidates.Add(Path.GetFullPath(Path.Combine(dir, includePath)));

            foreach (var root in _roots)
                candidates.Add(Path.GetFullPath(Path.Combine(root, includePath)));
        }

        var outsideRoot = false;
        foreach (var candidate in candidates.Distinct(OperatingSystem.IsWindows()
                     ? StringComparer.OrdinalIgnoreCase
                     : StringComparer.Ordinal))
        {
            if (!File.Exists(candidate))
                continue;

            if (IsAllowed(candidate))
                return (candidate, null, false);

            outsideRoot = true;
        }

        return outsideRoot
            ? (null, $"Refusing to include file outside the allowed roots: '{includePath}'.", true)
            : (null, $"Could not resolve include '{includePath}'.", false);
    }

    private void AddRoot(string path)
    {
        var full = Path.GetFullPath(path);
        if (!_roots.Contains(full, OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal))
        {
            _roots.Add(full);
        }
    }

    private bool IsAllowed(string fullPath)
    {
        var full = Path.GetFullPath(fullPath);
        return _roots.Any(root => IsUnder(root, full));
    }

    private static bool IsUnder(string root, string path)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var normalizedPath = Path.GetFullPath(path);

        if (normalizedPath.Equals(normalizedRoot, PathComparison))
            return true;

        var prefix = normalizedRoot.EndsWith(Path.DirectorySeparatorChar)
            ? normalizedRoot
            : normalizedRoot + Path.DirectorySeparatorChar;
        return normalizedPath.StartsWith(prefix, PathComparison);
    }

    private static IEnumerable<string> EnumerateRuleFiles(string directory)
    {
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories);
        }
        catch
        {
            yield break;
        }

        foreach (var file in files)
        {
            var extension = Path.GetExtension(file);
            if (!extension.Equals(".yar", StringComparison.OrdinalIgnoreCase) &&
                !extension.Equals(".yara", StringComparison.OrdinalIgnoreCase))
                continue;

            var segments = file.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (segments.Any(s => s.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                                  s.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
                                  s.Equals(".git", StringComparison.OrdinalIgnoreCase)))
                continue;

            yield return file;
        }
    }
}
