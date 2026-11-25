using YaraTranslator.Parsing;

namespace YaraTranslator.Semantics;

public static class TargetDetector
{
    public static TargetKind? ResolvePrimaryTarget(YaraRule rule, TranslationOptions options)
    {
        switch (options.Target)
        {
            case TargetSelection.Pe:
                return TargetKind.Pe;
            case TargetSelection.Script:
                return TargetKind.Script;
            case TargetSelection.Both:
                return null;
        }

        var metaTarget = rule.FindMeta("target");
        if (metaTarget?.Value is string text)
        {
            if (text.Equals("script", StringComparison.OrdinalIgnoreCase))
                return TargetKind.Script;
            if (text.Equals("pe", StringComparison.OrdinalIgnoreCase))
                return TargetKind.Pe;
        }

        // The scanner's PE heuristic database is the default for otherwise unclassified rules.
        return TargetKind.Pe;
    }

    public static bool UsesPe(ConditionExpr? expr) => expr is not null && Walk(expr);

    private static bool Walk(ConditionExpr expr) => expr switch
    {
        MemberExpr member => IsPeName(member.Member) || Walk(member.Target),
        FunctionCallExpr call => IsPeName(call.Name) || call.Name.StartsWith("uint16", StringComparison.OrdinalIgnoreCase)
                                 || call.Arguments.Any(Walk),
        IdentifierExpr identifier => identifier.Name.Equals("entrypoint", StringComparison.OrdinalIgnoreCase) ||
                                     identifier.Name.Equals("pe", StringComparison.OrdinalIgnoreCase),
        UnaryExpr unary => Walk(unary.Operand),
        BinaryExpr binary => Walk(binary.Left) || Walk(binary.Right),
        AndExpr and => and.Items.Any(Walk),
        OrExpr or => or.Items.Any(Walk),
        NotExpr not => Walk(not.Inner),
        IndexExpr index => Walk(index.Target) || Walk(index.Index),
        OfExpr of => of.RangeExpression is not null && Walk(of.RangeExpression),
        ForExpr @for => (@for.Body is not null && Walk(@for.Body)) || (@for.Range is not null && Walk(@for.Range)),
        _ => false
    };

    private static bool IsPeName(string name)
        => name.Equals("pe", StringComparison.OrdinalIgnoreCase) ||
           name.StartsWith("pe.", StringComparison.OrdinalIgnoreCase);
}
