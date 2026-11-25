namespace YaraTranslator.Semantics;

public abstract record BoolExpr;

public sealed record BoolAtom(string RuleName, string StringId, string VariantId) : BoolExpr;

public sealed record BoolConst(bool Value) : BoolExpr;

public sealed record BoolAnd(IReadOnlyList<BoolExpr> Items) : BoolExpr;

public sealed record BoolOr(IReadOnlyList<BoolExpr> Items) : BoolExpr;

public sealed record BoolNot(BoolExpr Inner) : BoolExpr;

public sealed record BoolUnsupported(string Reason) : BoolExpr;

public sealed record BoolLiteral(string RuleName, string StringId, string VariantId, bool Negated)
{
    public string Key => $"{RuleName}:{StringId}:{VariantId}";
}

public sealed class BoolAlgebra
{
    public static BoolExpr True => new BoolConst(true);

    public static BoolExpr False => new BoolConst(false);

    public static BoolExpr And(params BoolExpr[] items) => Simplify(new BoolAnd(items));

    public static BoolExpr Or(params BoolExpr[] items) => Simplify(new BoolOr(items));

    public static BoolExpr Not(BoolExpr item) => Simplify(new BoolNot(item));

    public static BoolExpr Simplify(BoolExpr expr)
    {
        switch (expr)
        {
            case BoolAtom:
            case BoolConst:
            case BoolUnsupported:
                return expr;

            case BoolNot not:
            {
                var inner = Simplify(not.Inner);
                if (inner is BoolConst c)
                    return new BoolConst(!c.Value);
                if (inner is BoolUnsupported)
                    return inner;
                if (inner is BoolNot nested)
                    return Simplify(nested.Inner);
                if (inner is BoolAnd and)
                    return Simplify(new BoolOr(and.Items.Select(i => new BoolNot(i)).ToArray()));
                if (inner is BoolOr or)
                    return Simplify(new BoolAnd(or.Items.Select(i => new BoolNot(i)).ToArray()));
                return new BoolNot(inner);
            }

            case BoolAnd and:
            {
                var items = new List<BoolExpr>();
                BoolUnsupported? unsupported = null;

                foreach (var raw in and.Items)
                {
                    var item = Simplify(raw);
                    if (item is BoolConst c)
                    {
                        if (!c.Value)
                            return False;
                        continue;
                    }

                    if (item is BoolUnsupported u)
                    {
                        unsupported ??= u;
                        continue;
                    }

                    if (item is BoolAnd nested)
                        items.AddRange(nested.Items);
                    else
                        items.Add(item);
                }

                if (unsupported is not null)
                    return unsupported;

                var deduped = Deduplicate(items);
                if (HasContradiction(deduped))
                    return False;
                if (deduped.Count == 0)
                    return True;
                if (deduped.Count == 1)
                    return deduped[0];
                return new BoolAnd(deduped);
            }

            case BoolOr or:
            {
                var items = new List<BoolExpr>();
                BoolUnsupported? unsupported = null;

                foreach (var raw in or.Items)
                {
                    var item = Simplify(raw);
                    if (item is BoolConst c)
                    {
                        if (c.Value)
                            return True;
                        continue;
                    }

                    if (item is BoolUnsupported u)
                    {
                        unsupported ??= u;
                        continue;
                    }

                    if (item is BoolOr nested)
                        items.AddRange(nested.Items);
                    else
                        items.Add(item);
                }

                if (unsupported is not null)
                    return unsupported;

                var deduped = Deduplicate(items);
                if (HasTautology(deduped))
                    return True;
                if (deduped.Count == 0)
                    return False;
                if (deduped.Count == 1)
                    return deduped[0];
                return new BoolOr(deduped);
            }

            default:
                throw new InvalidOperationException("Unknown boolean expression.");
        }
    }

    public static IReadOnlyList<IReadOnlyList<BoolLiteral>> ToDnf(BoolExpr expr, int maxTerms)
    {
        var simplified = Simplify(expr);
        if (simplified is BoolUnsupported unsupported)
            throw new ExpansionLimitException(unsupported.Reason);

        var terms = ToDnfCore(simplified, maxTerms);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<IReadOnlyList<BoolLiteral>>();

        foreach (var term in terms)
        {
            var sorted = term.OrderBy(l => l.Key, StringComparer.Ordinal)
                .ThenBy(l => l.Negated)
                .ToList();
            var key = string.Join("&", sorted.Select(l => (l.Negated ? "!" : string.Empty) + l.Key));
            if (seen.Add(key))
                result.Add(sorted);
        }

        return result;
    }

    private static List<IReadOnlyList<BoolLiteral>> ToDnfCore(BoolExpr expr, int maxTerms)
    {
        switch (expr)
        {
            case BoolAtom atom:
                return new List<IReadOnlyList<BoolLiteral>>
                {
                    new List<BoolLiteral> { new(atom.RuleName, atom.StringId, atom.VariantId, false) }
                };

            case BoolNot not when not.Inner is BoolAtom atom:
                return new List<IReadOnlyList<BoolLiteral>>
                {
                    new List<BoolLiteral> { new(atom.RuleName, atom.StringId, atom.VariantId, true) }
                };

            case BoolConst constant:
                return constant.Value
                    ? new List<IReadOnlyList<BoolLiteral>> { new List<BoolLiteral>() }
                    : new List<IReadOnlyList<BoolLiteral>>();

            case BoolAnd and:
            {
                var current = new List<IReadOnlyList<BoolLiteral>> { new List<BoolLiteral>() };
                foreach (var item in and.Items)
                {
                    var right = ToDnfCore(item, maxTerms);
                    if (right.Count == 0)
                        return new List<IReadOnlyList<BoolLiteral>>();

                    if ((long)current.Count * right.Count > maxTerms)
                        throw new ExpansionLimitException($"Condition expansion exceeds {maxTerms} terms.");

                    var next = new List<IReadOnlyList<BoolLiteral>>();
                    foreach (var leftTerm in current)
                    {
                        foreach (var rightTerm in right)
                        {
                            var merged = MergeTerms(leftTerm, rightTerm);
                            if (merged is not null)
                                next.Add(merged);
                        }
                    }

                    current = next;
                    if (current.Count > maxTerms)
                        throw new ExpansionLimitException($"Condition expansion exceeds {maxTerms} terms.");
                }

                return current;
            }

            case BoolOr or:
            {
                var result = new List<IReadOnlyList<BoolLiteral>>();
                foreach (var item in or.Items)
                {
                    var terms = ToDnfCore(item, maxTerms);
                    if ((long)result.Count + terms.Count > maxTerms)
                        throw new ExpansionLimitException($"Condition expansion exceeds {maxTerms} terms.");
                    result.AddRange(terms);
                }

                return result;
            }

            case BoolUnsupported unsupported:
                throw new ExpansionLimitException(unsupported.Reason);

            default:
                throw new ExpansionLimitException("Unsupported boolean expression shape.");
        }
    }

    private static IReadOnlyList<BoolLiteral>? MergeTerms(
        IReadOnlyList<BoolLiteral> left,
        IReadOnlyList<BoolLiteral> right)
    {
        var merged = new Dictionary<string, BoolLiteral>(StringComparer.Ordinal);

        foreach (var literal in left.Concat(right))
        {
            if (merged.TryGetValue(literal.Key, out var existing))
            {
                if (existing.Negated != literal.Negated)
                    return null; // contradiction: A and not A
                continue;
            }

            merged.Add(literal.Key, literal);
        }

        return merged.Values.ToList();
    }

    private static List<BoolExpr> Deduplicate(IEnumerable<BoolExpr> items)
    {
        var result = new List<BoolExpr>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var item in items)
        {
            var key = Key(item);
            if (seen.Add(key))
                result.Add(item);
        }

        return result;
    }

    private static bool HasContradiction(IReadOnlyList<BoolExpr> items)
    {
        var positives = new HashSet<string>(StringComparer.Ordinal);
        var negatives = new HashSet<string>(StringComparer.Ordinal);

        foreach (var item in items)
        {
            if (item is BoolAtom atom)
                positives.Add(atom.RuleName + ":" + atom.StringId + ":" + atom.VariantId);
            else if (item is BoolNot { Inner: BoolAtom inner })
                negatives.Add(inner.RuleName + ":" + inner.StringId + ":" + inner.VariantId);
        }

        return positives.Overlaps(negatives);
    }

    private static bool HasTautology(IReadOnlyList<BoolExpr> items)
    {
        var positives = new HashSet<string>(StringComparer.Ordinal);
        var negatives = new HashSet<string>(StringComparer.Ordinal);

        foreach (var item in items)
        {
            if (item is BoolAtom atom)
                positives.Add(atom.RuleName + ":" + atom.StringId + ":" + atom.VariantId);
            else if (item is BoolNot { Inner: BoolAtom inner })
                negatives.Add(inner.RuleName + ":" + inner.StringId + ":" + inner.VariantId);
        }

        return positives.Overlaps(negatives);
    }

    private static string Key(BoolExpr expr) => expr switch
    {
        BoolAtom atom => $"A:{atom.RuleName}:{atom.StringId}:{atom.VariantId}",
        BoolConst constant => constant.Value ? "T" : "F",
        BoolNot not => "N:" + Key(not.Inner),
        BoolAnd and => "&(" + string.Join(",", and.Items.Select(Key).OrderBy(k => k, StringComparer.Ordinal)) + ")",
        BoolOr or => "|(" + string.Join(",", or.Items.Select(Key).OrderBy(k => k, StringComparer.Ordinal)) + ")",
        BoolUnsupported unsupported => "U:" + unsupported.Reason,
        _ => expr.ToString() ?? "?"
    };
}
