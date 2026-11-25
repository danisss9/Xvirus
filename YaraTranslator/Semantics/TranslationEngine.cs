using YaraTranslator.Parsing;

namespace YaraTranslator.Semantics;

public sealed class TranslationEngine
{
    private readonly TranslationOptions _options;

    public TranslationEngine(TranslationOptions options)
    {
        _options = options;
    }

    public TranslationResult Translate(IEnumerable<string> inputs)
    {
        var result = new TranslationResult();
        var inputList = inputs.Where(i => !string.IsNullOrWhiteSpace(i)).ToList();
        if (inputList.Count == 0)
        {
            result.Diagnostics.Add(new Diagnostic("YARA1301", DiagnosticSeverity.Error, "No input rule files were specified."));
            result.RecalculateSummary();
            return result;
        }

        var loaded = new SourceLoader(_options).Load(inputList);
        result.Diagnostics.AddRange(loaded.Diagnostics.Items);
        result.SourceFiles.AddRange(loaded.Files);

        var rules = new List<YaraRule>();
        var rulesByName = new Dictionary<string, YaraRule>(StringComparer.Ordinal);
        foreach (var rule in loaded.Rules)
        {
            if (rulesByName.ContainsKey(rule.Name))
            {
                result.Diagnostics.Add(new Diagnostic(
                    "YARA1302",
                    DiagnosticSeverity.Error,
                    $"Duplicate rule name '{rule.Name}'. The first definition is kept.",
                    rule.Span,
                    rule.Name));
                continue;
            }

            rulesByName.Add(rule.Name, rule);
            rules.Add(rule);
        }

        if (rules.Count == 0)
        {
            result.Diagnostics.Add(new Diagnostic("YARA1303", DiagnosticSeverity.Warning, "No YARA rules were found in the selected inputs."));
            result.RecalculateSummary();
            return result;
        }

        var globalRuleNames = rules.Where(r => r.IsGlobal).Select(r => r.Name).ToList();
        var targetsByRule = new Dictionary<string, List<TargetKind>>(StringComparer.Ordinal);
        var allTargets = new HashSet<TargetKind>();

        foreach (var rule in rules)
        {
            var targets = ResolveTargets(rule);
            targetsByRule[rule.Name] = targets;
            foreach (var target in targets)
                allTargets.Add(target);
        }

        var semanticsByTarget = new Dictionary<TargetKind, Dictionary<string, RuleSemantics>>();
        foreach (var target in allTargets)
        {
            var semantics = new Dictionary<string, RuleSemantics>(StringComparer.Ordinal);
            var lowering = new StringLowering(_options);
            // Lower every rule for each active target so cross-target rule references and
            // global rules can be resolved consistently.
            foreach (var rule in rules)
                semantics[rule.Name] = BuildSemantics(rule, target, lowering);
            semanticsByTarget[target] = semantics;
        }

        var translations = new Dictionary<string, RuleTranslation>(StringComparer.Ordinal);
        var seenDiagnostics = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var rule in rules)
        {
            translations[rule.Name] = new RuleTranslation
            {
                Rule = rule,
                TargetSelection = _options.Target
            };
            seenDiagnostics[rule.Name] = new HashSet<string>(StringComparer.Ordinal);
        }

        var outcomes = new Dictionary<string, List<TargetOutcome>>(StringComparer.Ordinal);
        foreach (var rule in rules)
            outcomes[rule.Name] = new List<TargetOutcome>();

        foreach (var target in allTargets)
        {
            var semantics = semanticsByTarget[target];
            var lowerer = new ConditionLowerer(_options, target, semantics, globalRuleNames);
            var plannedRules = new List<PlannedRule>();

            foreach (var rule in rules)
            {
                if (!targetsByRule[rule.Name].Contains(target))
                    continue;

                var translation = translations[rule.Name];
                var ruleSemantics = semantics[rule.Name];

                AddDiagnostics(translation, ruleSemantics.Diagnostics, seenDiagnostics[rule.Name]);

                if (rule.IsPrivate || rule.IsGlobal)
                    continue;

                var planned = BuildPlannedRule(rule, ruleSemantics, semantics, translation, lowerer, seenDiagnostics[rule.Name]);
                plannedRules.Add(planned);
            }

            var planner = new SignaturePlanner(_options);
            planner.Plan(target, plannedRules);

            foreach (var planned in plannedRules)
            {
                var translation = planned.Translation;
                AddDiagnostics(translation, planned.Diagnostics, seenDiagnostics[planned.Semantics.Rule.Name]);
                outcomes[planned.Semantics.Rule.Name].Add(new TargetOutcome(
                    planned.NotApplicable,
                    planned.Failed,
                    planned.Status,
                    planned.Lines.Count));
            }
        }

        foreach (var rule in rules)
        {
            var translation = translations[rule.Name];

            if (rule.IsPrivate || rule.IsGlobal)
            {
                translation.Status = RuleStatus.Skipped;
                AddDiagnostics(translation, new[]
                {
                    new Diagnostic(
                        rule.IsPrivate ? "YARA1304" : "YARA1305",
                        DiagnosticSeverity.Info,
                        rule.IsPrivate
                            ? "Private rule is not emitted; it is inlined when referenced."
                            : "Global rule is not emitted; it gates the other rules in the namespace.",
                        rule.Span,
                        rule.Name)
                }, seenDiagnostics[rule.Name]);
                continue;
            }

            var relevant = outcomes[rule.Name];
            if (translation.Lines.Count == 0)
            {
                translation.Status = RuleStatus.Skipped;
                if (relevant.Count > 0 && relevant.All(o => o.NotApplicable) && !relevant.Any(o => o.Failed))
                {
                    AddDiagnostics(translation, new[]
                    {
                        new Diagnostic(
                            "YARA1306",
                            DiagnosticSeverity.Info,
                            "Rule condition does not apply to the selected target or is always false.",
                            rule.Span,
                            rule.Name)
                    }, seenDiagnostics[rule.Name]);
                }

                continue;
            }

            var anyFailure = relevant.Any(o => o.Failed);
            var status = relevant
                .Where(o => !o.NotApplicable && !o.Failed)
                .Select(o => o.Status)
                .DefaultIfEmpty(RuleStatus.Exact)
                .Aggregate((left, right) => left.Worst(right));

            if (anyFailure)
                status = status.Worst(RuleStatus.Partial);

            translation.Status = status;
        }

        foreach (var translation in translations.Values)
            result.Rules.Add(translation);

        foreach (var translation in translations.Values)
            result.Lines.AddRange(translation.Lines);

        if (result.Lines.Count > _options.MaxTotalLines)
        {
            result.Diagnostics.Add(new Diagnostic(
                "YARA1307",
                DiagnosticSeverity.Error,
                $"Translation produced {result.Lines.Count} lines, above the limit of {_options.MaxTotalLines}."));
        }

        result.RecalculateSummary();
        return result;
    }

    private List<TargetKind> ResolveTargets(YaraRule rule)
    {
        return _options.Target switch
        {
            TargetSelection.Pe => new List<TargetKind> { TargetKind.Pe },
            TargetSelection.Script => new List<TargetKind> { TargetKind.Script },
            TargetSelection.Both => new List<TargetKind> { TargetKind.Pe, TargetKind.Script },
            _ => new List<TargetKind> { TargetDetector.ResolvePrimaryTarget(rule, _options) ?? TargetKind.Pe }
        };
    }

    private static RuleSemantics BuildSemantics(YaraRule rule, TargetKind target, StringLowering lowering)
    {
        var semantics = new RuleSemantics { Rule = rule, Target = target };

        foreach (var source in rule.Strings)
        {
            var lowered = lowering.Lower(source, rule.Name);
            if (semantics.Strings.ContainsKey(source.Id))
            {
                semantics.Diagnostics.Add(new Diagnostic(
                    "YARA1308",
                    DiagnosticSeverity.Warning,
                    $"Duplicate string identifier '{source.Id}'; the first declaration is used.",
                    source.Span,
                    rule.Name));
                continue;
            }

            semantics.Strings.Add(source.Id, lowered);
            semantics.Diagnostics.AddRange(lowered.Diagnostics);
            semantics.Approximated |= lowered.Approximated;
            semantics.HasUnusableString |= lowered.Unusable;
            semantics.Features.UnionWith(lowered.Features);

            foreach (var variant in lowered.Variants)
                semantics.AllVariants.Add(variant);
        }

        return semantics;
    }

    private PlannedRule BuildPlannedRule(
        YaraRule rule,
        RuleSemantics semantics,
        IReadOnlyDictionary<string, RuleSemantics> semanticsByRule,
        RuleTranslation translation,
        ConditionLowerer lowerer,
        HashSet<string> seenDiagnostics)
    {
        var translated = lowerer.Translate(rule);
        AddDiagnostics(translation, translated.Diagnostics, seenDiagnostics);

        var planned = new PlannedRule
        {
            Semantics = semantics,
            Translation = translation,
            Approximated = translated.Approximated || semantics.Approximated
        };

        if (translated.Expr is BoolUnsupported unsupported)
        {
            Fail(planned, "YARA1309", $"Rule cannot be translated: {unsupported.Reason}");
            return planned;
        }

        IReadOnlyList<IReadOnlyList<BoolLiteral>> dnf;
        try
        {
            dnf = BoolAlgebra.ToDnf(translated.Expr, _options.MaxTermsPerRule);
        }
        catch (ExpansionLimitException ex)
        {
            Fail(planned, "YARA1310", $"Condition expansion failed: {ex.Message}");
            return planned;
        }

        if (dnf.Count == 0)
        {
            planned.NotApplicable = true;
            return planned;
        }

        foreach (var literals in dnf)
        {
            var positive = new Dictionary<string, AtomRef>(StringComparer.Ordinal);
            var negative = new Dictionary<string, AtomRef>(StringComparer.Ordinal);
            var invalid = false;

            foreach (var literal in literals)
            {
                if (!semanticsByRule.TryGetValue(literal.RuleName, out var sourceSemantics))
                {
                    Fail(planned, "YARA1311",
                        $"Rule '{literal.RuleName}' was not lowered for the selected target while planning '{rule.Name}'.");
                    invalid = true;
                    break;
                }

                var variant = sourceSemantics.FindVariant(literal.StringId, literal.VariantId);
                if (variant is null)
                {
                    Fail(planned, "YARA1311",
                        $"Pattern variant '{literal.VariantId}' from rule '{literal.RuleName}' was not found while planning '{rule.Name}'.");
                    invalid = true;
                    break;
                }

                var atom = new AtomRef(literal.RuleName, literal.StringId, literal.VariantId, variant.Hex, variant.ByteLength);
                if (literal.Negated)
                    negative[atom.Hex] = atom;
                else
                    positive[atom.Hex] = atom;
            }

            if (invalid)
                return planned;

            // A term that contains both P and !P is false and can be dropped safely.
            if (positive.Keys.Any(negative.ContainsKey))
                continue;

            if (positive.Count == 0)
            {
                Fail(planned, "YARA1312",
                    "A condition term has no positive literal that can act as an anchor.");
                return planned;
            }

            var term = new PlannedTerm();
            term.Positive.AddRange(positive.Values);
            term.Negative.AddRange(negative.Values);
            planned.Terms.Add(term);
        }

        if (planned.Terms.Count == 0)
        {
            planned.NotApplicable = true;
            return planned;
        }

        if (planned.Terms.Count > _options.MaxLinesPerRule)
        {
            Fail(planned, "YARA1313",
                $"Rule requires {planned.Terms.Count} terms, above the limit of {_options.MaxLinesPerRule}.");
        }

        return planned;
    }

    private static void Fail(PlannedRule planned, string code, string message)
    {
        planned.Failed = true;
        planned.Status = RuleStatus.Skipped;
        planned.Diagnostics.Add(new Diagnostic(
            code,
            DiagnosticSeverity.Warning,
            message,
            planned.Semantics.Rule.Span,
            planned.Semantics.Rule.Name));
    }

    private static void AddDiagnostics(
        RuleTranslation translation,
        IEnumerable<Diagnostic> diagnostics,
        HashSet<string> seen)
    {
        foreach (var diagnostic in diagnostics)
        {
            var key = $"{diagnostic.Code}|{diagnostic.Severity}|{diagnostic.Message}|{diagnostic.Span}";
            if (seen.Add(key))
                translation.Diagnostics.Add(diagnostic);
        }
    }

    private sealed record TargetOutcome(
        bool NotApplicable,
        bool Failed,
        RuleStatus Status,
        int LineCount);
}
