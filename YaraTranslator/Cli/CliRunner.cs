using System.Text.Json;
using YaraTranslator.Emit;
using YaraTranslator.Parsing;
using YaraTranslator.Semantics;
using YaraTranslator.Validation;

namespace YaraTranslator.Cli;

public static class CliRunner
{
    public static int Run(string[] args)
    {
        var parsed = CommandLine.Parse(args);
        if (parsed.Error is not null)
        {
            Console.Error.WriteLine(parsed.Error);
            Console.Error.WriteLine("Use 'YaraTranslator help' for usage.");
            return 1;
        }

        var options = parsed.Options!;
        if (options.Help || options.Command == "help")
        {
            PrintHelp();
            return 0;
        }

        try
        {
            return options.Command switch
            {
                "translate" => Translate(options),
                "validate" => Validate(options),
                "explain" => Explain(options),
                "version" => Version(),
                _ => 1
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Unhandled error: {ex.Message}");
            return 1;
        }
    }

    private static int Translate(CliOptions cli)
    {
        if (cli.Inputs.Count == 0)
        {
            Console.Error.WriteLine("No input rule files were specified.");
            return 1;
        }

        var options = cli.Translation;
        var engine = new TranslationEngine(options);
        var result = engine.Translate(cli.Inputs);

        PrintDiagnostics(result, options.Quiet);

        var resultTargets = result.Lines.Select(l => l.Target).Distinct().ToList();
        var forceDirectory = resultTargets.Count > 1;
        var outRoot = Path.GetFullPath(options.OutputPath ?? "out");
        if (forceDirectory && File.Exists(outRoot))
        {
            Console.Error.WriteLine("The selected target produces more than one fragment, so --out must be a directory.");
            return 1;
        }

        if (forceDirectory && !Directory.Exists(outRoot) && !string.IsNullOrEmpty(Path.GetExtension(outRoot)))
        {
            Console.Error.WriteLine("The selected target produces more than one fragment, so --out must be a directory, not a file name.");
            return 1;
        }

        var outIsFile = !forceDirectory &&
                        (File.Exists(outRoot) || !string.IsNullOrEmpty(Path.GetExtension(outRoot)));
        var outDir = outIsFile
            ? Path.GetDirectoryName(outRoot) ?? Directory.GetCurrentDirectory()
            : outRoot;

        if (outIsFile && resultTargets.Count > 1)
        {
            Console.Error.WriteLine("A single output file cannot receive both PE and script fragments; use an output directory.");
            return 1;
        }

        var reportPath = Path.GetFullPath(options.ReportPath ?? Path.Combine(outDir, "report.json"));
        var manifestPath = Path.GetFullPath(options.ManifestPath ?? Path.Combine(outDir, "yara-manifest.json"));
        var sourceHash = ManifestStore.ComputeSourceHash(result.SourceFiles);
        var report = ReportWriter.Build(result, options, result.SourceFiles, sourceHash);

        if (result.Diagnostics.Any(d => d.Code == "YARA1307"))
        {
            if (!options.DryRun)
                ReportWriter.Write(report, reportPath);
            return 1;
        }

        if (options.DryRun)
        {
            if (!options.Quiet)
            {
                PrintSummary(result, Array.Empty<DbWriteResult>(), reportPath, manifestPath, dryRun: true);
            }

            return DetermineExitCode(result, cli);
        }

        Directory.CreateDirectory(outDir);

        Manifest? previous = options.Prune ? ManifestStore.Read(manifestPath) : null;
        var writes = new List<DbWriteResult>();
        var acceptedKeys = new HashSet<string>(StringComparer.Ordinal);
        var writeErrors = new List<string>();

        foreach (var target in resultTargets)
        {
            var lines = result.LinesFor(target).ToList();
            if (lines.Count == 0)
                continue;

            var path = outIsFile
                ? outRoot
                : Path.Combine(outDir, target == TargetKind.Pe ? "heurlist.yara.db" : "heurlist2.yara.db");

            string? mergePath;
            try
            {
                mergePath = ResolveMergePath(options.MergeDatabase, target, resultTargets.Count > 1);
            }
            catch (Exception ex)
            {
                writeErrors.Add(ex.Message);
                continue;
            }

            var pruneKeys = previous is null
                ? null
                : previous.Entries
                    .Where(e => e.Target == (target == TargetKind.Pe ? "pe" : "script"))
                    .Select(e => e.CanonicalKey)
                    .ToHashSet(StringComparer.Ordinal);

            try
            {
                var writer = new HeuristicDbWriter();
                var write = writer.Write(lines, path, mergePath, options.CollisionPolicy, options.Prune ? pruneKeys : null);
                writes.Add(write);
                acceptedKeys.UnionWith(write.AcceptedKeys);
            }
            catch (DbMergeException ex)
            {
                writeErrors.Add(ex.Message);
            }
            catch (Exception ex)
            {
                writeErrors.Add($"Could not write '{path}': {ex.Message}");
            }
        }

        try
        {
            ReportWriter.Write(report, reportPath);
        }
        catch (Exception ex)
        {
            writeErrors.Add($"Could not write report '{reportPath}': {ex.Message}");
        }

        if (writeErrors.Count == 0 && writes.Count > 0)
        {
            try
            {
                var manifest = ManifestStore.Build(result, sourceHash, acceptedKeys);
                ManifestStore.Write(manifest, manifestPath);
            }
            catch (Exception ex)
            {
                writeErrors.Add($"Could not write manifest '{manifestPath}': {ex.Message}");
            }
        }

        if (!options.Quiet)
            PrintSummary(result, writes, reportPath, manifestPath, dryRun: false);

        foreach (var error in writeErrors)
            Console.Error.WriteLine(error);

        if (writeErrors.Count > 0)
            return 1;

        return DetermineExitCode(result, cli);
    }

    private static string? ResolveMergePath(string? merge, TargetKind target, bool multipleTargets)
    {
        if (string.IsNullOrWhiteSpace(merge))
            return null;

        if (!multipleTargets)
        {
            if (Directory.Exists(merge))
                throw new InvalidOperationException(
                    "When translating a single target, --merge must point to a database file, not a directory.");

            return Path.GetFullPath(merge);
        }

        if (!Directory.Exists(merge))
            throw new InvalidOperationException(
                "When translating both PE and script targets, --merge must point to a directory containing heurlist.db and heurlist2.db.");

        return Path.Combine(Path.GetFullPath(merge), target == TargetKind.Pe ? "heurlist.db" : "heurlist2.db");
    }

    private static int Validate(CliOptions cli)
    {
        if (cli.Inputs.Count == 0)
        {
            Console.Error.WriteLine("No database paths were specified.");
            return 1;
        }

        var results = HeuristicDbValidator.ValidateAll(cli.Inputs);
        var hadError = false;

        foreach (var result in results)
        {
            Console.WriteLine($"{result.Path}: {result.Entries} entries, {result.Lines} lines");
            foreach (var diagnostic in result.Diagnostics.OrderBy(d => d.Severity))
            {
                if (cli.Translation.Quiet && diagnostic.Severity == DiagnosticSeverity.Info)
                    continue;
                Console.WriteLine($"  {diagnostic}");
            }

            if (result.HasErrors)
                hadError = true;
        }

        return hadError ? 1 : 0;
    }

    private static int Explain(CliOptions cli)
    {
        if (cli.Inputs.Count == 0)
        {
            Console.Error.WriteLine("No input rule files were specified.");
            return 1;
        }

        var options = cli.Translation;
        var engine = new TranslationEngine(options);
        var result = engine.Translate(cli.Inputs);

        if (cli.Json)
        {
            var payload = new
            {
                toolVersion = "0.1.0",
                engineProfile = options.Engine == EngineProfile.Strict ? "strict" : "legacy",
                target = options.Target.ToString().ToLowerInvariant(),
                summary = result.Summary,
                rules = result.Rules.Select(rule => new
                {
                    name = rule.Rule.Name,
                    file = rule.Rule.File,
                    line = rule.Rule.Line,
                    status = rule.Status.ToString().ToLowerInvariant(),
                    targets = rule.Lines.Select(l => l.Target == TargetKind.Pe ? "pe" : "script").Distinct().ToArray(),
                    features = rule.Features.OrderBy(f => f, StringComparer.Ordinal).ToArray(),
                    lines = rule.Lines.Select(line => new
                    {
                        anchor = line.AnchorHex,
                        name = line.Name,
                        dependencies = line.Dependencies.Select(d => d.ToDbText()).ToArray(),
                        target = line.Target == TargetKind.Pe ? "pe" : "script",
                        dbLine = line.ToDbLine()
                    }).ToArray(),
                    diagnostics = rule.Diagnostics.Select(ReportWriter.ToReportDiagnostic).ToArray()
                }).ToArray(),
                diagnostics = result.Diagnostics.Select(ReportWriter.ToReportDiagnostic).ToArray()
            };

            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
            Console.WriteLine(json);
        }
        else
        {
            PrintDiagnostics(result, options.Quiet);

            foreach (var rule in result.Rules.OrderBy(r => r.Rule.Name, StringComparer.Ordinal))
            {
                Console.WriteLine($"{rule.Rule.Name} [{rule.Status.ToString().ToLowerInvariant()}] {rule.Lines.Count} line(s)");
                foreach (var line in rule.Lines)
                {
                    Console.WriteLine($"  {(line.Target == TargetKind.Pe ? "pe" : "script")}  {line.ToDbLine()}");
                }

                foreach (var diagnostic in rule.Diagnostics)
                    Console.WriteLine($"  {diagnostic}");
            }
        }

        return result.Summary.Errors > 0 ? 1 : 0;
    }

    private static int Version()
    {
        Console.WriteLine("YaraTranslator 0.1.0");
        return 0;
    }

    private static void PrintDiagnostics(TranslationResult result, bool quiet)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var diagnostic in result.Diagnostics.Concat(result.Rules.SelectMany(r => r.Diagnostics)))
        {
            if (quiet && diagnostic.Severity == DiagnosticSeverity.Info)
                continue;

            var key = $"{diagnostic.Code}|{diagnostic.Severity}|{diagnostic.Message}|{diagnostic.Span}";
            if (!seen.Add(key))
                continue;

            if (diagnostic.Severity == DiagnosticSeverity.Error)
                Console.Error.WriteLine(diagnostic);
            else
                Console.WriteLine(diagnostic);
        }
    }

    private static void PrintSummary(
        TranslationResult result,
        IReadOnlyList<DbWriteResult> writes,
        string reportPath,
        string manifestPath,
        bool dryRun)
    {
        Console.WriteLine($"Rules: {result.Summary.Rules} " +
                          $"(exact {result.Summary.Exact}, conditional {result.Summary.Conditional}, " +
                          $"partial {result.Summary.Partial}, skipped {result.Summary.Skipped})");
        Console.WriteLine($"Lines: {result.Summary.Lines} " +
                          $"(PE {result.Summary.PeLines}, script {result.Summary.ScriptLines})");

        if (writes.Count == 0)
        {
            Console.WriteLine("No database fragment was written.");
        }
        else
        {
            foreach (var write in writes)
            {
                Console.WriteLine(
                    $"Wrote {write.Path}: {write.Lines} entries " +
                    $"(added {write.Added}, replaced {write.Replaced}, skipped {write.Skipped}, pruned {write.Pruned})");
            }
        }

        if (dryRun)
        {
            Console.WriteLine($"Dry run: report would be written to {reportPath}");
            Console.WriteLine($"Dry run: manifest would be written to {manifestPath}");
        }
        else
        {
            Console.WriteLine($"Report: {reportPath}");
            Console.WriteLine($"Manifest: {manifestPath}");
        }
    }

    private static int DetermineExitCode(TranslationResult result, CliOptions cli)
    {
        if (result.Summary.Errors > 0)
            return 1;

        var partial = result.Rules.Any(r => r.Status is RuleStatus.Partial or RuleStatus.Skipped);
        var warning = result.Summary.Warnings > 0 || partial;

        return cli.FailOn switch
        {
            FailOn.None => 0,
            FailOn.Partial => partial ? 2 : 0,
            FailOn.Warning => warning ? 2 : 0,
            _ => 0
        };
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
YaraTranslator 0.1.0 - translate YARA rules to Xvirus heuristic database entries.

Usage:
  YaraTranslator translate <paths...> [options]
  YaraTranslator validate <db...> [--quiet]
  YaraTranslator explain <paths...> [options] [--json]
  YaraTranslator version
  YaraTranslator help

Translate options:
  --out <dir|file>             Output directory or single fragment file. Default: out
  --target auto|pe|script|both Target database. Default: auto
  --engine legacy|strict       Assumed scanner behavior for reporting. Default: strict
  --allow-approximations       Emit best-effort rules instead of skipping them
  --merge <db|dir>             Merge into an existing database (directory for both targets)
  --collision error|skip|keep|overwrite
  --name-template <template>   Placeholders: {rule}, {namespace}, {file}, {tag}
  --namespace <name>           Namespace value for {namespace}
  --min-pattern-bytes <n>      Default: 4
  --max-pattern-bytes <n>      Default: 4096
  --max-variants <n>           Default: 256
  --max-terms-per-rule <n>     Default: 1024
  --max-lines-per-rule <n>     Default: 4096
  --max-input-bytes <n>        Default: 16777216
  --include-dir <path>         Repeatable include search directory
  --define KEY=VALUE           Repeatable external variable
  --manifest <file>            Default: <out>/yara-manifest.json
  --report <file>              Default: <out>/report.json
  --prune                      Remove lines owned by the previous manifest
  --dry-run                    Translate and report without writing files
  --fail-on none|partial|warning
  --quiet

Exit codes: 0 success, 1 error, 2 partial/skipped (or warnings with --fail-on warning).
""");
    }
}
