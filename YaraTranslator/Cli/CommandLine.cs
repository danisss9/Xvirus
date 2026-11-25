using System.Globalization;
using YaraTranslator.Semantics;

namespace YaraTranslator.Cli;

public enum FailOn
{
    None,
    Partial,
    Warning
}

public sealed class CliOptions
{
    public string Command { get; set; } = string.Empty;
    public List<string> Inputs { get; } = new();
    public TranslationOptions Translation { get; } = new();
    public FailOn FailOn { get; set; } = FailOn.Partial;
    public bool Json { get; set; }
    public bool Help { get; set; }
}

public sealed record CliParseResult(CliOptions? Options, string? Error)
{
    public static CliParseResult Success(CliOptions options) => new(options, null);
    public static CliParseResult Failure(string error) => new(null, error);
}

public static class CommandLine
{
    public static CliParseResult Parse(string[] args)
    {
        if (args.Length == 0)
        {
            var help = new CliOptions { Command = "help", Help = true };
            return CliParseResult.Success(help);
        }

        var command = args[0].ToLowerInvariant();
        var options = new CliOptions { Command = command };

        if (command is "help" or "-h" or "--help" or "-?")
        {
            options.Command = "help";
            options.Help = true;
            return CliParseResult.Success(options);
        }

        if (command == "version")
            return CliParseResult.Success(options);

        if (command is not ("translate" or "validate" or "explain"))
            return CliParseResult.Failure($"Unknown command '{args[0]}'. Use translate, validate, explain, version or help.");

        for (var i = 1; i < args.Length; i++)
        {
            var arg = args[i];

            if (arg == "--")
            {
                for (var j = i + 1; j < args.Length; j++)
                    options.Inputs.Add(args[j]);
                break;
            }

            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                if (arg is "-h" or "--help" or "-?")
                {
                    options.Help = true;
                    continue;
                }

                options.Inputs.Add(arg);
                continue;
            }

            var equals = arg.IndexOf('=');
            var name = equals >= 0 ? arg[..equals] : arg;
            var inlineValue = equals >= 0 ? arg[(equals + 1)..] : null;

            switch (name)
            {
                case "--help":
                    options.Help = true;
                    break;

                case "--allow-approximations":
                    options.Translation.AllowApproximations = true;
                    break;

                case "--prune":
                    options.Translation.Prune = true;
                    break;

                case "--dry-run":
                    options.Translation.DryRun = true;
                    break;

                case "--quiet":
                    options.Translation.Quiet = true;
                    break;

                case "--json":
                    options.Json = true;
                    break;

                case "--out":
                case "--merge":
                case "--report":
                case "--manifest":
                case "--name-template":
                case "--namespace":
                case "--target":
                case "--engine":
                case "--collision":
                case "--fail-on":
                case "--min-pattern-bytes":
                case "--max-pattern-bytes":
                case "--max-variants":
                case "--max-terms-per-rule":
                case "--max-lines-per-rule":
                case "--max-input-bytes":
                case "--include-dir":
                case "--define":
                {
                    var valueResult = ReadValue(args, ref i, inlineValue);
                    if (valueResult.Error is not null)
                        return CliParseResult.Failure($"{name}: {valueResult.Error}");

                    var value = valueResult.Value!;
                    switch (name)
                    {
                        case "--out":
                            options.Translation.OutputPath = value;
                            break;
                        case "--merge":
                            options.Translation.MergeDatabase = value;
                            break;
                        case "--report":
                            options.Translation.ReportPath = value;
                            break;
                        case "--manifest":
                            options.Translation.ManifestPath = value;
                            break;
                        case "--name-template":
                            options.Translation.NameTemplate = value;
                            break;
                        case "--namespace":
                            options.Translation.Namespace = value;
                            break;
                        case "--target":
                            if (!TryParseTarget(value, out var target))
                                return CliParseResult.Failure($"Invalid --target '{value}'. Use auto, pe, script or both.");
                            options.Translation.Target = target;
                            break;
                        case "--engine":
                            if (!TryParseEngine(value, out var engine))
                                return CliParseResult.Failure($"Invalid --engine '{value}'. Use legacy or strict.");
                            options.Translation.Engine = engine;
                            break;
                        case "--collision":
                            if (!TryParseCollision(value, out var collision))
                                return CliParseResult.Failure($"Invalid --collision '{value}'. Use error, skip, keep or overwrite.");
                            options.Translation.CollisionPolicy = collision;
                            break;
                        case "--fail-on":
                            if (!TryParseFailOn(value, out var failOn))
                                return CliParseResult.Failure($"Invalid --fail-on '{value}'. Use none, partial or warning.");
                            options.FailOn = failOn;
                            break;
                        case "--min-pattern-bytes":
                            if (!TryParsePositiveInt(value, out var min))
                                return CliParseResult.Failure("--min-pattern-bytes must be a positive integer.");
                            options.Translation.MinPatternBytes = min;
                            break;
                        case "--max-pattern-bytes":
                            if (!TryParsePositiveInt(value, out var maxPattern))
                                return CliParseResult.Failure("--max-pattern-bytes must be a positive integer.");
                            options.Translation.MaxPatternBytes = maxPattern;
                            break;
                        case "--max-variants":
                            if (!TryParsePositiveInt(value, out var maxVariants))
                                return CliParseResult.Failure("--max-variants must be a positive integer.");
                            options.Translation.MaxVariantsPerString = maxVariants;
                            break;
                        case "--max-terms-per-rule":
                            if (!TryParsePositiveInt(value, out var maxTerms))
                                return CliParseResult.Failure("--max-terms-per-rule must be a positive integer.");
                            options.Translation.MaxTermsPerRule = maxTerms;
                            break;
                        case "--max-lines-per-rule":
                            if (!TryParsePositiveInt(value, out var maxLines))
                                return CliParseResult.Failure("--max-lines-per-rule must be a positive integer.");
                            options.Translation.MaxLinesPerRule = maxLines;
                            break;
                        case "--max-input-bytes":
                            if (!TryParsePositiveInt(value, out var maxInput))
                                return CliParseResult.Failure("--max-input-bytes must be a positive integer.");
                            options.Translation.MaxInputBytes = maxInput;
                            break;
                        case "--include-dir":
                            options.Translation.IncludeDirectories.Add(value);
                            break;
                        case "--define":
                            if (!TryParseDefine(value, out var key, out var defineValue))
                                return CliParseResult.Failure($"Invalid --define '{value}'. Use KEY=VALUE.");
                            options.Translation.ExternalVariables[key] = defineValue;
                            break;
                    }

                    break;
                }

                default:
                    return CliParseResult.Failure($"Unknown option '{name}'.");
            }
        }

        if (options.Help)
            options.Command = "help";

        return CliParseResult.Success(options);
    }

    private static (string? Value, string? Error) ReadValue(string[] args, ref int index, string? inlineValue)
    {
        if (inlineValue is not null)
            return (inlineValue, null);

        if (index + 1 >= args.Length)
            return (null, "missing value");

        index++;
        return (args[index], null);
    }

    private static bool TryParseTarget(string value, out TargetSelection target)
    {
        switch (value.ToLowerInvariant())
        {
            case "auto":
                target = TargetSelection.Auto;
                return true;
            case "pe":
                target = TargetSelection.Pe;
                return true;
            case "script":
                target = TargetSelection.Script;
                return true;
            case "both":
                target = TargetSelection.Both;
                return true;
            default:
                target = TargetSelection.Auto;
                return false;
        }
    }

    private static bool TryParseEngine(string value, out EngineProfile engine)
    {
        switch (value.ToLowerInvariant())
        {
            case "legacy":
                engine = EngineProfile.Legacy;
                return true;
            case "strict":
                engine = EngineProfile.Strict;
                return true;
            default:
                engine = EngineProfile.Legacy;
                return false;
        }
    }

    private static bool TryParseCollision(string value, out CollisionPolicy policy)
    {
        switch (value.ToLowerInvariant())
        {
            case "error":
                policy = CollisionPolicy.Error;
                return true;
            case "skip":
                policy = CollisionPolicy.Skip;
                return true;
            case "keep":
                policy = CollisionPolicy.Keep;
                return true;
            case "overwrite":
                policy = CollisionPolicy.Overwrite;
                return true;
            default:
                policy = CollisionPolicy.Error;
                return false;
        }
    }

    private static bool TryParseFailOn(string value, out FailOn failOn)
    {
        switch (value.ToLowerInvariant())
        {
            case "none":
                failOn = FailOn.None;
                return true;
            case "partial":
                failOn = FailOn.Partial;
                return true;
            case "warning":
                failOn = FailOn.Warning;
                return true;
            default:
                failOn = FailOn.Partial;
                return false;
        }
    }

    private static bool TryParsePositiveInt(string value, out int result)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result) && result > 0;

    private static bool TryParseDefine(string value, out string key, out object? defineValue)
    {
        var equals = value.IndexOf('=');
        if (equals <= 0)
        {
            key = string.Empty;
            defineValue = null;
            return false;
        }

        key = value[..equals];
        var raw = value[(equals + 1)..];

        if (raw.Equals("true", StringComparison.OrdinalIgnoreCase))
            defineValue = true;
        else if (raw.Equals("false", StringComparison.OrdinalIgnoreCase))
            defineValue = false;
        else if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
            defineValue = number;
        else if (raw.Length >= 2 && raw[0] == '"' && raw[^1] == '"')
            defineValue = raw[1..^1];
        else
            defineValue = raw;

        return true;
    }
}
