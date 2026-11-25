using YaraTranslator.Parsing;

namespace YaraTranslator.Semantics;

public sealed record Dependency(string PatternHex, bool Negated)
{
    public string ToDbText() => Negated ? "!" + PatternHex : PatternHex;
}

public sealed class SignatureLine
{
    public required string AnchorHex { get; init; }
    public required string Name { get; init; }
    public required IReadOnlyList<Dependency> Dependencies { get; init; }
    public required TargetKind Target { get; init; }
    public required string RuleName { get; init; }
    public string SourceRuleName { get; init; } = string.Empty;

    public string ToDbLine()
    {
        var deps = Dependencies.Count == 0
            ? string.Empty
            : "," + string.Join("&", Dependencies.Select(d => d.ToDbText()));
        return $"{AnchorHex},{Name}{deps}";
    }

    public string Key => $"{Target}|{AnchorHex}|{Name}|{string.Join("&", Dependencies.Select(d => d.ToDbText()))}";

    public bool IsSelfDependency => Dependencies.Count == 1 &&
                                    !Dependencies[0].Negated &&
                                    Dependencies[0].PatternHex == AnchorHex;
}

public sealed class RuleTranslation
{
    public required YaraRule Rule { get; init; }
    public TargetSelection TargetSelection { get; init; }
    public RuleStatus Status { get; set; } = RuleStatus.Skipped;
    public List<SignatureLine> Lines { get; } = new();
    public List<Diagnostic> Diagnostics { get; } = new();
    public HashSet<string> Features { get; } = new(StringComparer.OrdinalIgnoreCase);

    public IEnumerable<TargetKind> Targets => Lines.Select(l => l.Target).Distinct();
}

public sealed class TranslationSummary
{
    public int Rules { get; set; }
    public int Exact { get; set; }
    public int Conditional { get; set; }
    public int Partial { get; set; }
    public int Skipped { get; set; }
    public int Lines { get; set; }
    public int PeLines { get; set; }
    public int ScriptLines { get; set; }
    public int Errors { get; set; }
    public int Warnings { get; set; }
}

public sealed class TranslationResult
{
    public List<RuleTranslation> Rules { get; } = new();
    public List<SignatureLine> Lines { get; } = new();
    public List<Diagnostic> Diagnostics { get; } = new();
    public List<string> SourceFiles { get; } = new();
    public TranslationSummary Summary { get; } = new();

    public IEnumerable<SignatureLine> LinesFor(TargetKind target)
        => Lines.Where(l => l.Target == target);

    public void RecalculateSummary()
    {
        Summary.Rules = Rules.Count;
        Summary.Exact = Rules.Count(r => r.Status == RuleStatus.Exact);
        Summary.Conditional = Rules.Count(r => r.Status == RuleStatus.Conditional);
        Summary.Partial = Rules.Count(r => r.Status == RuleStatus.Partial);
        Summary.Skipped = Rules.Count(r => r.Status == RuleStatus.Skipped);
        Summary.Lines = Lines.Count;
        Summary.PeLines = Lines.Count(l => l.Target == TargetKind.Pe);
        Summary.ScriptLines = Lines.Count(l => l.Target == TargetKind.Script);
        Summary.Errors = Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error) +
                         Rules.SelectMany(r => r.Diagnostics).Count(d => d.Severity == DiagnosticSeverity.Error);
        Summary.Warnings = Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning) +
                           Rules.SelectMany(r => r.Diagnostics).Count(d => d.Severity == DiagnosticSeverity.Warning);
    }
}

public static class RuleStatusExtensions
{
    public static RuleStatus Worst(this RuleStatus left, RuleStatus right)
    {
        return (RuleStatus)Math.Max((int)left, (int)right);
    }
}
