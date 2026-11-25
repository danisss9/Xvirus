using System.Text.RegularExpressions;
using YaraTranslator.Parsing;

namespace YaraTranslator.Semantics;

public sealed record TranslatedCondition(
    BoolExpr Expr,
    bool Approximated,
    IReadOnlyList<Diagnostic> Diagnostics);

/// <summary>
/// Lowers a parsed YARA condition to a Boolean expression over literal pattern variants.
/// The lowering is deliberately conservative: unsupported features become
/// <see cref="BoolUnsupported"/> unless an explicit, documented approximation is enabled.
/// </summary>
public sealed class ConditionLowerer
{
    private readonly TranslationOptions _options;
    private readonly TargetKind _target;
    private readonly IReadOnlyDictionary<string, RuleSemantics> _rules;
    private readonly IReadOnlyList<string> _globalRuleNames;
    private readonly Dictionary<(string Rule, TargetKind Target), TranslatedCondition> _cache = new();
    private readonly HashSet<(string Rule, TargetKind Target)> _resolving = new();

    public ConditionLowerer(
        TranslationOptions options,
        TargetKind target,
        IReadOnlyDictionary<string, RuleSemantics> rules,
        IReadOnlyList<string> globalRuleNames)
    {
        _options = options;
        _target = target;
        _rules = rules;
        _globalRuleNames = globalRuleNames;
    }

    public TranslatedCondition Translate(YaraRule rule)
    {
        var key = (rule.Name, _target);
        if (_cache.TryGetValue(key, out var cached))
            return cached;

        if (!_resolving.Add(key))
            return new TranslatedCondition(
                new BoolUnsupported($"Circular rule reference involving '{rule.Name}'."),
                false,
                new[]
                {
                    new Diagnostic(
                        "YARA3001",
                        DiagnosticSeverity.Error,
                        $"Circular rule reference involving '{rule.Name}'.",
                        rule.Span,
                        rule.Name)
                });

        try
        {
            if (!_rules.TryGetValue(rule.Name, out var semantics))
                return Cache(key, rule, new BoolUnsupported($"Rule '{rule.Name}' could not be lowered."));

            var context = new ConditionContext(rule.Name);
            var expr = rule.Condition is null
                ? Unsupported(context, "YARA3002", "Rule has no condition.", rule.Span)
                : ToBool(rule.Condition, context);

            if (!rule.IsGlobal)
            {
                foreach (var globalName in _globalRuleNames)
                {
                    if (globalName.Equals(rule.Name, StringComparison.Ordinal))
                        continue;
                    if (!_rules.TryGetValue(globalName, out var globalSemantics))
                        continue;

                    var global = Translate(globalSemantics.Rule);
                    context.Diagnostics.AddRange(global.Diagnostics);
                    context.Approximated |= global.Approximated;

                    if (global.Expr is BoolUnsupported unsupported)
                    {
                        Unsupported(context, "YARA3003", $"Global rule '{globalName}' is not representable: {unsupported.Reason}", rule.Span);
                        continue;
                    }

                    expr = BoolAlgebra.And(expr, global.Expr);
                }
            }

            expr = BoolAlgebra.Simplify(expr);
            if (context.HardUnsupported is not null || expr is BoolUnsupported)
            {
                var reason = context.HardUnsupported ?? (expr as BoolUnsupported)?.Reason ?? "Unsupported condition.";
                var diagnostics = new List<Diagnostic>(semantics.Diagnostics);
                diagnostics.AddRange(context.Diagnostics);
                var result = new TranslatedCondition(
                    new BoolUnsupported(reason),
                    context.Approximated || semantics.Approximated,
                    diagnostics);
                _cache[key] = result;
                return result;
            }

            var successful = new List<Diagnostic>(semantics.Diagnostics);
            successful.AddRange(context.Diagnostics);
            var translated = new TranslatedCondition(
                expr,
                context.Approximated || semantics.Approximated,
                successful);
            _cache[key] = translated;
            return translated;
        }
        finally
        {
            _resolving.Remove(key);
        }
    }

    private TranslatedCondition Cache((string Rule, TargetKind Target) key, YaraRule rule, BoolExpr expr)
    {
        var diagnostics = new List<Diagnostic>();
        var approximated = false;

        if (_rules.TryGetValue(rule.Name, out var semantics))
        {
            diagnostics.AddRange(semantics.Diagnostics);
            approximated = semantics.Approximated;

            if (expr is BoolUnsupported unsupported)
            {
                diagnostics.Add(new Diagnostic(
                    "YARA3004",
                    DiagnosticSeverity.Warning,
                    $"Rule '{rule.Name}' skipped: {unsupported.Reason}",
                    rule.Span,
                    rule.Name));
            }
        }

        var result = new TranslatedCondition(expr, approximated, diagnostics);
        _cache[key] = result;
        return result;
    }

    private BoolExpr ToBool(ConditionExpr expr, ConditionContext context)
    {
        switch (expr)
        {
            case BoolLiteralExpr literal:
                return new BoolConst(literal.Value);

            case StringMatchExpr match:
                return StringMatchToBool(match.Id, context, match.Span);

            case StringCountExpr count:
                return Unsupported(context, "YARA3005",
                    $"String count '{count.Id}' is only supported inside a comparison.", count.Span);

            case StringOffsetExpr offset:
                return Unsupported(context, "YARA3006",
                    $"String offset '{offset.Id}' is not representable by the heuristic engine.", offset.Span);

            case StringLengthExpr length:
                return Unsupported(context, "YARA3007",
                    $"Matched-string length '{length.Id}' is not representable by the heuristic engine.", length.Span);

            case IdentifierExpr identifier:
                return IdentifierToBool(identifier, context);

            case MemberExpr member:
                return MemberToBool(member, context);

            case FunctionCallExpr call:
                return FunctionToBool(call, context);

            case UnaryExpr unary:
                return UnaryToBool(unary, context);

            case BinaryExpr binary:
                return BinaryToBool(binary, context);

            case AndExpr and:
            {
                var items = and.Items.Select(item => ToBool(item, context)).ToArray();
                return BoolAlgebra.And(items);
            }

            case OrExpr or:
            {
                var items = or.Items.Select(item => ToBool(item, context)).ToArray();
                return BoolAlgebra.Or(items);
            }

            case NotExpr not:
                return BoolAlgebra.Not(ToBool(not.Inner, context));

            case OfExpr of:
                return OfToBool(of, context);

            case ForExpr:
                return Unsupported(context, "YARA3008",
                    "'for' quantifiers are not supported by the heuristic engine.", expr.Span);

            case RegexLiteralExpr:
            case TextLiteralExpr:
            case IntLiteralExpr:
            case FloatLiteralExpr:
                return Unsupported(context, "YARA3009",
                    "A literal value cannot be used as a boolean condition.", expr.Span);

            case UnsupportedExpr unsupported:
                return Unsupported(context, "YARA3010", unsupported.Reason, unsupported.Span);

            default:
                return Unsupported(context, "YARA3011",
                    $"Unsupported condition expression '{expr.GetType().Name}'.", expr.Span);
        }
    }

    private BoolExpr StringMatchToBool(string stringId, ConditionContext context, SourceSpan span)
    {
        stringId = NormalizeStringId(stringId);
        var semantics = _rules[context.RuleName];
        if (!semantics.Strings.TryGetValue(stringId, out var lowered))
        {
            return Unsupported(context, "YARA3012",
                $"String '{stringId}' is not declared in rule '{context.RuleName}'.", span);
        }

        if (lowered.Unusable || lowered.Variants.Count == 0)
        {
            return Unsupported(context, "YARA3013",
                $"String '{stringId}' has no usable literal pattern.", span);
        }

        var atoms = lowered.Variants
            .Select(v => (BoolExpr)new BoolAtom(context.RuleName, stringId, v.VariantId))
            .ToArray();

        return atoms.Length == 1 ? atoms[0] : BoolAlgebra.Or(atoms);
    }

    private BoolExpr IdentifierToBool(IdentifierExpr identifier, ConditionContext context)
    {
        var name = identifier.Name;

        if (name.Equals("filesize", StringComparison.OrdinalIgnoreCase))
        {
            if (!_options.AllowApproximations)
            {
                return Unsupported(context, "YARA3014",
                    "filesize is not representable by the heuristic engine.", identifier.Span);
            }

            context.Approximate("YARA3015", "filesize constraint ignored (approximation).", identifier.Span);
            return BoolAlgebra.True;
        }

        if (name.Equals("entrypoint", StringComparison.OrdinalIgnoreCase))
        {
            return Unsupported(context, "YARA3016",
                "entrypoint is not representable by the heuristic engine.", identifier.Span);
        }

        if (_options.ExternalVariables.TryGetValue(name, out var value))
        {
            return value switch
            {
                bool b => new BoolConst(b),
                _ => Unsupported(context, "YARA3017",
                    $"External variable '{name}' is not boolean.", identifier.Span)
            };
        }

        if (_rules.TryGetValue(name, out var referenced))
        {
            var translated = Translate(referenced.Rule);
            context.Diagnostics.AddRange(translated.Diagnostics);
            context.Approximated |= translated.Approximated;
            return translated.Expr;
        }

        // A rule name that is not known, or a module/property identifier, cannot be used
        // as a boolean without a runtime.
        return Unsupported(context, "YARA3018",
            $"Unknown identifier '{name}' cannot be evaluated as a boolean.", identifier.Span);
    }

    private BoolExpr MemberToBool(MemberExpr member, ConditionContext context)
    {
        if (IsPeIsPe(member))
            return new BoolConst(_target == TargetKind.Pe);

        return Unsupported(context, "YARA3019",
            $"Member expression '{FlattenName(member)}' is not supported.", member.Span);
    }

    private BoolExpr FunctionToBool(FunctionCallExpr call, ConditionContext context)
    {
        if (call.Name.Equals("pe.is_pe", StringComparison.OrdinalIgnoreCase))
            return new BoolConst(_target == TargetKind.Pe);

        return Unsupported(context, "YARA3020",
            $"Function '{call.Name}' is not supported by the heuristic engine.", call.Span);
    }

    private BoolExpr UnaryToBool(UnaryExpr unary, ConditionContext context)
    {
        if (unary.Operator.Equals("defined", StringComparison.OrdinalIgnoreCase))
        {
            if (unary.Operand is IdentifierExpr identifier &&
                _options.ExternalVariables.ContainsKey(identifier.Name))
            {
                return BoolAlgebra.True;
            }

            return Unsupported(context, "YARA3021",
                "'defined' is not supported for this operand.", unary.Span);
        }

        if (unary.Operator == "not")
            return BoolAlgebra.Not(ToBool(unary.Operand, context));

        return Unsupported(context, "YARA3022",
            $"Unary operator '{unary.Operator}' cannot be used as a boolean condition.", unary.Span);
    }

    private BoolExpr BinaryToBool(BinaryExpr binary, ConditionContext context)
    {
        var op = binary.Operator.ToLowerInvariant();

        if (op is "and" or "or")
        {
            var left = ToBool(binary.Left, context);
            var right = ToBool(binary.Right, context);
            return op == "and" ? BoolAlgebra.And(left, right) : BoolAlgebra.Or(left, right);
        }

        if (op is "at" or "in")
        {
            if (!_options.AllowApproximations)
            {
                return Unsupported(context, "YARA3023",
                    $"Match {op} constraint is not representable by the heuristic engine.", binary.Span);
            }

            context.Approximate("YARA3024", $"Match {op} constraint ignored (approximation).", binary.Span);
            return ToBool(binary.Left, context);
        }

        if (op is "==" or "!=" or "<" or "<=" or ">" or ">=")
        {
            if (TryMzComparison(binary, op, out var mzResult))
                return new BoolConst(mzResult);

            if (TryConstantComparison(binary, op, out var constantResult))
                return new BoolConst(constantResult);

            var leftCount = binary.Left as StringCountExpr;
            var rightCount = binary.Right as StringCountExpr;

            if (leftCount is not null && TryEvaluateInt(binary.Right, out var rightValue))
                return CountComparison(leftCount.Id, op, rightValue, context, binary.Span);

            if (rightCount is not null && TryEvaluateInt(binary.Left, out var leftValue))
                return CountComparison(rightCount.Id, FlipComparison(op), leftValue, context, binary.Span);

            if (ContainsFilesize(binary.Left) || ContainsFilesize(binary.Right))
            {
                if (!_options.AllowApproximations)
                {
                    return Unsupported(context, "YARA3025",
                        "filesize is not representable by the heuristic engine.", binary.Span);
                }

                context.Approximate("YARA3026", "filesize constraint ignored (approximation).", binary.Span);
                return BoolAlgebra.True;
            }

            if (binary.Left is StringOffsetExpr || binary.Right is StringOffsetExpr ||
                binary.Left is StringLengthExpr || binary.Right is StringLengthExpr)
            {
                return Unsupported(context, "YARA3027",
                    "String offsets and lengths are not representable by the heuristic engine.", binary.Span);
            }

            return Unsupported(context, "YARA3028",
                $"Comparison '{op}' is not supported for these operands.", binary.Span);
        }

        if (op is "contains" or "icontains" or "startswith" or "istartswith" or
            "endswith" or "iendswith" or "iequals" or "matches")
        {
            if (TryConstantStringOperation(binary, op, out var result))
                return new BoolConst(result);

            return Unsupported(context, "YARA3029",
                $"String operator '{op}' is not supported for these operands.", binary.Span);
        }

        return Unsupported(context, "YARA3030",
            $"Operator '{op}' cannot be used as a boolean condition.", binary.Span);
    }

    private BoolExpr OfToBool(OfExpr of, ConditionContext context)
    {
        if (of.RangeKind is not null)
        {
            if (!_options.AllowApproximations)
            {
                return Unsupported(context, "YARA3031",
                    $"'of' {of.RangeKind} constraint is not representable by the heuristic engine.", of.Span);
            }

            context.Approximate("YARA3032", $"'of' {of.RangeKind} constraint ignored (approximation).", of.Span);
        }

        var ids = ResolveStringSet(of.Set, context, of.Span);
        if (context.HardUnsupported is not null)
            return new BoolUnsupported(context.HardUnsupported);

        if (ids.Count == 0)
        {
            return Unsupported(context, "YARA3033",
                "The 'of' expression selects no strings.", of.Span);
        }

        var stringBools = new List<(string Id, BoolExpr Expr)>();
        foreach (var id in ids)
        {
            var boolExpr = StringMatchToBool(id, context, of.Span);
            if (boolExpr is BoolUnsupported)
                return boolExpr;
            stringBools.Add((id, boolExpr));
        }

        switch (of.Quantifier.Kind)
        {
            case OfQuantifierKind.All:
                return BoolAlgebra.And(stringBools.Select(i => i.Expr).ToArray());

            case OfQuantifierKind.Any:
                return BoolAlgebra.Or(stringBools.Select(i => i.Expr).ToArray());

            case OfQuantifierKind.None:
                return BoolAlgebra.Not(BoolAlgebra.Or(stringBools.Select(i => i.Expr).ToArray()));

            case OfQuantifierKind.Number:
                return NumberOf(of.Quantifier.Number, stringBools, context, of.Span);

            default:
                return Unsupported(context, "YARA3034",
                    "Unknown 'of' quantifier.", of.Span);
        }
    }

    private BoolExpr NumberOf(
        long count,
        IReadOnlyList<(string Id, BoolExpr Expr)> strings,
        ConditionContext context,
        SourceSpan span)
    {
        if (count <= 0)
            return BoolAlgebra.True;

        if (count > strings.Count)
            return BoolAlgebra.False;

        if (count == 1)
            return BoolAlgebra.Or(strings.Select(i => i.Expr).ToArray());

        if (count == strings.Count)
            return BoolAlgebra.And(strings.Select(i => i.Expr).ToArray());

        var combinationCount = CombinationCount(strings.Count, (int)count);
        if (combinationCount > _options.MaxTermsPerRule)
        {
            context.Unsupported("YARA3035",
                $"'{count} of {strings.Count}' requires {combinationCount} combinations, above the limit of {_options.MaxTermsPerRule}.",
                span);
            return new BoolUnsupported("Condition expansion limit exceeded.");
        }

        var terms = new List<BoolExpr>();
        foreach (var indices in Combinations(strings.Count, (int)count))
        {
            var items = indices.Select(index => strings[index].Expr).ToArray();
            terms.Add(BoolAlgebra.And(items));
        }

        return BoolAlgebra.Or(terms.ToArray());
    }

    private IReadOnlyList<string> ResolveStringSet(StringSet set, ConditionContext context, SourceSpan span)
    {
        var semantics = _rules[context.RuleName];
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void Add(string id)
        {
            if (semantics.Strings.ContainsKey(id) && seen.Add(id))
                result.Add(id);
        }

        switch (set)
        {
            case AllStringsSet:
                foreach (var id in semantics.Strings.Keys)
                    Add(id);
                break;

            case NamedStringsSet named:
                foreach (var pattern in named.Patterns)
                {
                    if (pattern.Equals("them", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (var id in semantics.Strings.Keys)
                            Add(id);
                    }
                    else if (pattern.EndsWith('*'))
                    {
                        var prefix = pattern[..^1];
                        var matches = semantics.Strings.Keys
                            .Where(id => id.StartsWith(prefix, StringComparison.Ordinal))
                            .ToList();

                        if (matches.Count == 0)
                        {
                            context.Unsupported("YARA3039",
                                $"The string set pattern '{pattern}' matches no declared strings.", span);
                        }

                        foreach (var id in matches)
                            Add(id);
                    }
                    else if (semantics.Strings.ContainsKey(pattern))
                    {
                        Add(pattern);
                    }
                    else
                    {
                        context.Unsupported("YARA3040",
                            $"The string set references undeclared string '{pattern}'.", span);
                    }
                }

                break;
        }

        return result;
    }

    private BoolExpr CountComparison(
        string stringId,
        string op,
        long value,
        ConditionContext context,
        SourceSpan span)
    {
        stringId = NormalizeStringId(stringId);
        var semantics = _rules[context.RuleName];
        if (!semantics.Strings.TryGetValue(stringId, out var lowered) ||
            lowered.Unusable || lowered.Variants.Count == 0)
        {
            return Unsupported(context, "YARA3036",
                $"String count '{stringId}' refers to an unusable string.", span);
        }

        BoolExpr presence = StringMatchToBool(stringId, context, span);

        // Exact cases that only need presence or absence.
        if (op == ">=" && value <= 0)
            return BoolAlgebra.True;
        if (op == ">" && value < 0)
            return BoolAlgebra.True;
        if (op == ">=" && value == 1)
            return presence;
        if (op == ">" && value == 0)
            return presence;
        if (op == "!=" && value == 0)
            return presence;
        if (op == "==" && value == 0)
            return BoolAlgebra.Not(presence);
        if (op == "<" && value == 1)
            return BoolAlgebra.Not(presence);
        if (op == "<=" && value == 0)
            return BoolAlgebra.Not(presence);

        // Out-of-range counts are constant false.
        if ((op == "==" && value < 0) ||
            (op == "<" && value <= 0) ||
            (op == "<=" && value < 0))
        {
            return BoolAlgebra.False;
        }

        if ((op == "!=" && value < 0) ||
            (op == ">" && value < 0) ||
            (op == ">=" && value <= 0))
        {
            return BoolAlgebra.True;
        }

        if (!_options.AllowApproximations)
        {
            return Unsupported(context, "YARA3037",
                $"String count comparison '{stringId} {op} {value}' cannot be represented exactly; use --allow-approximations to approximate it.",
                span);
        }

        context.Approximate("YARA3038",
            $"String count comparison '{stringId} {op} {value}' approximated as presence or truth.",
            span);

        // For lower bounds and equality, presence is a useful over-approximation. For
        // upper bounds and inequality, the safest broad approximation is "true".
        if (op is ">=" or ">" or "==")
            return presence;
        return BoolAlgebra.True;
    }

    private bool TryConstantComparison(BinaryExpr binary, string op, out bool result)
    {
        result = false;

        if (TryEvaluateInt(binary.Left, out var left) && TryEvaluateInt(binary.Right, out var right))
        {
            result = op switch
            {
                "==" => left == right,
                "!=" => left != right,
                "<" => left < right,
                "<=" => left <= right,
                ">" => left > right,
                ">=" => left >= right,
                _ => false
            };
            return true;
        }

        if (TryEvaluateText(binary.Left, out var leftText) && TryEvaluateText(binary.Right, out var rightText))
        {
            result = op switch
            {
                "==" => leftText.Equals(rightText, StringComparison.Ordinal),
                "!=" => !leftText.Equals(rightText, StringComparison.Ordinal),
                "<" => string.CompareOrdinal(leftText, rightText) < 0,
                "<=" => string.CompareOrdinal(leftText, rightText) <= 0,
                ">" => string.CompareOrdinal(leftText, rightText) > 0,
                ">=" => string.CompareOrdinal(leftText, rightText) >= 0,
                _ => false
            };
            return true;
        }

        return false;
    }

    private bool TryConstantStringOperation(BinaryExpr binary, string op, out bool result)
    {
        result = false;

        if (!TryEvaluateText(binary.Left, out var left))
            return false;

        if (op == "matches")
        {
            if (binary.Right is RegexLiteralExpr regex)
            {
                try
                {
                    var options = RegexOptions.None;
                    if (regex.Flags.Contains('i'))
                        options |= RegexOptions.IgnoreCase;
                    if (regex.Flags.Contains('s'))
                        options |= RegexOptions.Singleline;
                    result = Regex.IsMatch(left, regex.Pattern, options, TimeSpan.FromMilliseconds(250));
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            return false;
        }

        if (!TryEvaluateText(binary.Right, out var right))
            return false;

        var comparison = op.Contains("icontains", StringComparison.Ordinal) ||
                         op.Contains("istartswith", StringComparison.Ordinal) ||
                         op.Contains("iendswith", StringComparison.Ordinal) ||
                         op == "iequals"
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        result = op switch
        {
            "contains" or "icontains" => left.Contains(right, comparison),
            "startswith" or "istartswith" => left.StartsWith(right, comparison),
            "endswith" or "iendswith" => left.EndsWith(right, comparison),
            "iequals" => left.Equals(right, StringComparison.OrdinalIgnoreCase),
            _ => false
        };

        return true;
    }

    private bool TryMzComparison(BinaryExpr binary, string op, out bool value)
    {
        var direct = TryMzValue(binary.Left, binary.Right, op);
        if (direct.HasValue)
        {
            value = direct.Value;
            return true;
        }

        var reversed = TryMzValue(binary.Right, binary.Left, FlipComparison(op));
        if (reversed.HasValue)
        {
            value = reversed.Value;
            return true;
        }

        value = false;
        return false;
    }

    private bool? TryMzValue(ConditionExpr left, ConditionExpr right, string op)
    {
        if (left is not FunctionCallExpr call ||
            call.Arguments.Count != 1 ||
            call.Arguments[0] is not IntLiteralExpr offset ||
            offset.Value != 0 ||
            !TryEvaluateInt(right, out var expected))
        {
            return null;
        }

        long mz;
        if (call.Name.Equals("uint16", StringComparison.OrdinalIgnoreCase))
            mz = 0x5A4D;
        else if (call.Name.Equals("uint16be", StringComparison.OrdinalIgnoreCase))
            mz = 0x4D5A;
        else
            return null;

        if (expected != mz)
            return null;

        var isMz = _target == TargetKind.Pe;
        return op switch
        {
            "==" => isMz,
            "!=" => !isMz,
            _ => null
        };
    }

    private static bool ContainsFilesize(ConditionExpr expr) => expr switch
    {
        IdentifierExpr identifier => identifier.Name.Equals("filesize", StringComparison.OrdinalIgnoreCase),
        BinaryExpr binary => ContainsFilesize(binary.Left) || ContainsFilesize(binary.Right),
        UnaryExpr unary => ContainsFilesize(unary.Operand),
        _ => false
    };

    private bool TryEvaluateInt(ConditionExpr expr, out long value)
    {
        switch (expr)
        {
            case IntLiteralExpr literal:
                value = literal.Value;
                return true;
            case FloatLiteralExpr:
                value = 0;
                return false;
            case IdentifierExpr identifier when _options.ExternalVariables.TryGetValue(identifier.Name, out var external):
                if (external is long l)
                {
                    value = l;
                    return true;
                }

                if (external is int i)
                {
                    value = i;
                    return true;
                }

                value = 0;
                return false;
            case UnaryExpr { Operator: "-" } unary when TryEvaluateInt(unary.Operand, out var operand):
                value = -operand;
                return true;
            case UnaryExpr { Operator: "~" } unary when TryEvaluateInt(unary.Operand, out var operand):
                value = ~operand;
                return true;
            case BinaryExpr binary when TryEvaluateInt(binary.Left, out var left) && TryEvaluateInt(binary.Right, out var right):
                if ((binary.Operator is "/" or "%" or "\\") && right == 0)
                {
                    value = 0;
                    return false;
                }

                value = binary.Operator switch
                {
                    "+" => left + right,
                    "-" => left - right,
                    "*" => left * right,
                    "/" => left / right,
                    "%" or "\\" => left % right,
                    "<<" => left << (int)right,
                    ">>" => left >> (int)right,
                    "&" => left & right,
                    "|" => left | right,
                    "^" => left ^ right,
                    _ => 0
                };
                return binary.Operator is "+" or "-" or "*" or "/" or "%" or "\\" or "<<" or ">>" or "&" or "|" or "^";
            default:
                value = 0;
                return false;
        }
    }

    private bool TryEvaluateText(ConditionExpr expr, out string value)
    {
        switch (expr)
        {
            case TextLiteralExpr literal:
                value = literal.Value;
                return true;
            case IdentifierExpr identifier when _options.ExternalVariables.TryGetValue(identifier.Name, out var external)
                                                && external is string text:
                value = text;
                return true;
            case BinaryExpr { Operator: "+" } binary when TryEvaluateText(binary.Left, out var left) && TryEvaluateText(binary.Right, out var right):
                value = left + right;
                return true;
            default:
                value = string.Empty;
                return false;
        }
    }

    private static string NormalizeStringId(string id)
        => id.Length > 0 && id[0] is '#' or '@' or '!' ? "$" + id[1..] : id;

    private static string FlipComparison(string op) => op switch
    {
        "<" => ">",
        "<=" => ">=",
        ">" => "<",
        ">=" => "<=",
        _ => op
    };

    private static long CombinationCount(int n, int k)
    {
        if (k < 0 || k > n)
            return 0;
        if (k == 0 || k == n)
            return 1;

        k = Math.Min(k, n - k);
        long result = 1;
        for (var i = 1; i <= k; i++)
        {
            result = result * (n - k + i) / i;
            if (result > int.MaxValue)
                return int.MaxValue;
        }

        return result;
    }

    private static IEnumerable<IReadOnlyList<int>> Combinations(int n, int k)
    {
        var indices = Enumerable.Range(0, k).ToArray();
        while (true)
        {
            yield return indices.ToArray();
            var i = k - 1;
            while (i >= 0 && indices[i] == n - k + i)
                i--;
            if (i < 0)
                yield break;
            indices[i]++;
            for (var j = i + 1; j < k; j++)
                indices[j] = indices[j - 1] + 1;
        }
    }

    private static BoolExpr Unsupported(ConditionContext context, string code, string message, SourceSpan span)
    {
        context.Unsupported(code, message, span);
        return new BoolUnsupported(message);
    }

    private static bool IsPeIsPe(MemberExpr member)
    {
        return member.Member.Equals("is_pe", StringComparison.OrdinalIgnoreCase) &&
               member.Target is IdentifierExpr { Name: var name } &&
               name.Equals("pe", StringComparison.OrdinalIgnoreCase);
    }

    private static string FlattenName(ConditionExpr expr) => expr switch
    {
        IdentifierExpr identifier => identifier.Name,
        MemberExpr member => $"{FlattenName(member.Target)}.{member.Member}",
        IndexExpr index => $"{FlattenName(index.Target)}[...]",
        _ => expr.GetType().Name
    };

    private sealed class ConditionContext
    {
        public ConditionContext(string ruleName)
        {
            RuleName = ruleName;
        }

        public string RuleName { get; }
        public List<Diagnostic> Diagnostics { get; } = new();
        public bool Approximated { get; set; }
        public string? HardUnsupported { get; private set; }

        public void Approximate(string code, string message, SourceSpan span)
        {
            Approximated = true;
            Diagnostics.Add(new Diagnostic(code, DiagnosticSeverity.Warning, message, span, RuleName));
        }

        public void Unsupported(string code, string message, SourceSpan span)
        {
            HardUnsupported ??= message;
            Diagnostics.Add(new Diagnostic(code, DiagnosticSeverity.Warning, message, span, RuleName));
        }
    }
}
