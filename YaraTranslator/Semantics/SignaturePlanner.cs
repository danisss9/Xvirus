using YaraTranslator.Parsing;

namespace YaraTranslator.Semantics;

public sealed record AtomRef(string RuleName, string StringId, string VariantId, string Hex, int ByteLength);

public sealed class PlannedTerm
{
    public List<AtomRef> Positive { get; } = new();
    public List<AtomRef> Negative { get; } = new();
    public AtomRef? Anchor { get; set; }
    public bool Failed { get; set; }
}

public sealed class PlannedRule
{
    public required RuleSemantics Semantics { get; init; }
    public required RuleTranslation Translation { get; init; }
    public List<PlannedTerm> Terms { get; } = new();
    public List<SignatureLine> Lines { get; } = new();
    public List<Diagnostic> Diagnostics { get; } = new();
    public bool NotApplicable { get; set; }
    public bool Failed { get; set; }
    public bool Approximated { get; set; }
    public RuleStatus Status { get; set; } = RuleStatus.Exact;
}

/// <summary>
/// Converts DNF terms into database lines. A single anchor key cannot be used by two
/// lines because DB.LoadDictionary keeps only the first name and dependency list for a
/// duplicate key, so anchors are assigned with a bipartite matching rather than a naive
/// "pick the longest string" rule.
/// </summary>
public sealed class SignaturePlanner
{
    private readonly TranslationOptions _options;

    public SignaturePlanner(TranslationOptions options)
    {
        _options = options;
    }

    public void Plan(TargetKind target, IReadOnlyList<PlannedRule> rules)
    {
        var active = new List<(PlannedRule Rule, PlannedTerm Term, List<AtomRef> Candidates)>();

        foreach (var rule in rules)
        {
            rule.NotApplicable = false;
            rule.Lines.Clear();

            if (rule.Failed)
                continue;

            if (rule.Terms.Count == 0)
            {
                rule.NotApplicable = true;
                rule.Diagnostics.Add(new Diagnostic(
                    "YARA4001",
                    DiagnosticSeverity.Info,
                    $"Rule '{rule.Semantics.Rule.Name}' does not apply to the {target} target.",
                    rule.Semantics.Rule.Span,
                    rule.Semantics.Rule.Name));
                continue;
            }

            if (rule.Terms.Count > _options.MaxLinesPerRule)
            {
                Fail(rule, "YARA4002",
                    $"Rule requires {rule.Terms.Count} lines, above the limit of {_options.MaxLinesPerRule}.");
                continue;
            }

            foreach (var term in rule.Terms)
            {
                if (term.Positive.Count == 0)
                {
                    Fail(rule, "YARA4003",
                        "A condition term has no positive string that can act as an anchor.");
                    break;
                }

                var candidates = term.Positive
                    .GroupBy(a => a.Hex, StringComparer.Ordinal)
                    .Select(g => g.OrderByDescending(a => a.ByteLength)
                        .ThenBy(a => a.VariantId, StringComparer.Ordinal)
                        .First())
                    .OrderByDescending(a => a.ByteLength)
                    .ThenBy(a => a.Hex, StringComparer.Ordinal)
                    .ToList();

                active.Add((rule, term, candidates));
            }
        }

        // Rules that failed validation never enter matching.
        active.RemoveAll(item => item.Rule.Failed);
        var remaining = active;

        while (true)
        {
            var assignments = AssignAnchors(remaining);
            var failedRules = new HashSet<PlannedRule>();

            foreach (var item in remaining)
            {
                if (!assignments.ContainsKey(item.Term))
                {
                    failedRules.Add(item.Rule);
                }
            }

            if (failedRules.Count == 0)
                break;

            foreach (var rule in failedRules)
            {
                Fail(rule, "YARA4004",
                    "No unique anchor pattern could be assigned. Another generated rule, or another term in this rule, uses the only candidate literals as anchors.");
            }

            remaining = remaining.Where(item => !failedRules.Contains(item.Rule)).ToList();
            if (remaining.Count == 0)
                break;
        }

        var assignmentsFinal = AssignAnchors(remaining);
        foreach (var item in remaining)
        {
            if (!assignmentsFinal.TryGetValue(item.Term, out var anchor))
            {
                Fail(item.Rule, "YARA4005",
                    "No unique anchor pattern could be assigned.");
                continue;
            }

            item.Term.Anchor = anchor;
        }

        foreach (var rule in rules)
        {
            if (rule.Failed)
                continue;

            foreach (var term in rule.Terms)
            {
                if (term.Anchor is null)
                {
                    Fail(rule, "YARA4006", "A condition term could not be assigned an anchor.");
                    break;
                }

                var line = BuildLine(rule, term, target);
                if (line is null)
                {
                    Fail(rule, "YARA4007", "A condition term produced an invalid database line.");
                    break;
                }

                if (rule.Lines.All(l => l.Key != line.Key))
                    rule.Lines.Add(line);
            }

            if (rule.Lines.Count > _options.MaxLinesPerRule)
                Fail(rule, "YARA4008",
                    $"Rule produced {rule.Lines.Count} lines, above the limit of {_options.MaxLinesPerRule}.");
        }

        foreach (var rule in rules)
        {
            if (rule.Failed)
                continue;

            if (rule.Lines.Count == 0)
            {
                rule.NotApplicable = true;
                continue;
            }

            rule.Translation.Lines.AddRange(rule.Lines);
            foreach (var feature in rule.Semantics.Features)
                rule.Translation.Features.Add(feature);

            rule.Status = rule.Status.Worst(DetermineStatus(rule));
        }
    }

    private SignatureLine? BuildLine(PlannedRule rule, PlannedTerm term, TargetKind target)
    {
        if (term.Anchor is null)
            return null;

        var dependencies = new List<Dependency>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var positive in term.Positive)
        {
            if (positive.Hex.Equals(term.Anchor.Hex, StringComparison.Ordinal))
                continue;
            if (term.Negative.Any(n => n.Hex.Equals(positive.Hex, StringComparison.Ordinal)))
                return null; // contradiction; should have been removed earlier
            if (seen.Add("+" + positive.Hex))
                dependencies.Add(new Dependency(positive.Hex, false));
        }

        foreach (var negative in term.Negative)
        {
            if (negative.Hex.Equals(term.Anchor.Hex, StringComparison.Ordinal))
                return null; // anchor must be present AND absent
            if (seen.Add("-" + negative.Hex))
                dependencies.Add(new Dependency(negative.Hex, true));
        }

        if (dependencies.Count == 0)
            dependencies.Add(new Dependency(term.Anchor.Hex, false));

        var name = BuildName(rule.Semantics.Rule);
        return new SignatureLine
        {
            AnchorHex = term.Anchor.Hex,
            Name = name,
            Dependencies = dependencies,
            Target = target,
            RuleName = rule.Semantics.Rule.Name,
            SourceRuleName = rule.Semantics.Rule.Name
        };
    }

    private RuleStatus DetermineStatus(PlannedRule rule)
    {
        if (rule.Approximated || rule.Semantics.Approximated)
            return RuleStatus.Partial;

        if (_options.Engine == EngineProfile.Legacy &&
            rule.Lines.Any(l => l.Dependencies.Any(d => d.Negated ||
                                                        (!d.Negated && d.PatternHex != l.AnchorHex))))
        {
            return RuleStatus.Conditional;
        }

        return RuleStatus.Exact;
    }

    private void Fail(PlannedRule rule, string code, string message)
    {
        rule.Failed = true;
        rule.Status = RuleStatus.Skipped;
        rule.Diagnostics.Add(new Diagnostic(
            code,
            DiagnosticSeverity.Warning,
            message,
            rule.Semantics.Rule.Span,
            rule.Semantics.Rule.Name));
    }

    private string BuildName(YaraRule rule)
    {
        var tag = rule.Tags.FirstOrDefault() ?? string.Empty;
        var file = Path.GetFileNameWithoutExtension(rule.File);
        var name = _options.NameTemplate
            .Replace("{rule}", rule.Name, StringComparison.Ordinal)
            .Replace("{namespace}", _options.Namespace, StringComparison.Ordinal)
            .Replace("{file}", file, StringComparison.Ordinal)
            .Replace("{tag}", tag, StringComparison.Ordinal);

        var chars = name.Select(c => char.IsControl(c) || c is ',' or '&' or '\r' or '\n' ? '_' : c).ToArray();
        name = new string(chars).Trim().Trim('.', ' ');

        if (name.Length == 0)
            name = "YARA." + rule.Name;
        if (name.Length > 120)
            name = name[..120];

        return name;
    }

    private static Dictionary<PlannedTerm, AtomRef> AssignAnchors(
        IReadOnlyList<(PlannedRule Rule, PlannedTerm Term, List<AtomRef> Candidates)> terms)
    {
        var owner = new Dictionary<string, (PlannedRule Rule, PlannedTerm Term, List<AtomRef> Candidates)>(StringComparer.Ordinal);
        var result = new Dictionary<PlannedTerm, AtomRef>();

        var ordered = terms
            .OrderBy(t => t.Candidates.Count)
            .ThenBy(t => t.Rule.Semantics.Rule.Name, StringComparer.Ordinal)
            .ThenBy(t => string.Join(",", t.Term.Positive.Select(a => a.Hex)), StringComparer.Ordinal)
            .ToList();

        foreach (var term in ordered)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            if (TryAssign(term, visited, owner))
                continue;
        }

        foreach (var pair in owner)
        {
            result[pair.Value.Term] = pair.Value.Candidates.First(c => c.Hex == pair.Key);
        }

        return result;
    }

    private static bool TryAssign(
        (PlannedRule Rule, PlannedTerm Term, List<AtomRef> Candidates) term,
        HashSet<string> visited,
        Dictionary<string, (PlannedRule Rule, PlannedTerm Term, List<AtomRef> Candidates)> owner)
    {
        foreach (var candidate in term.Candidates)
        {
            if (!visited.Add(candidate.Hex))
                continue;

            if (!owner.TryGetValue(candidate.Hex, out var current) || TryAssign(current, visited, owner))
            {
                owner[candidate.Hex] = term;
                term.Term.Anchor = candidate;
                return true;
            }
        }

        return false;
    }
}
