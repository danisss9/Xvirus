using YaraTranslator.Parsing;

namespace YaraTranslator.Semantics;

public sealed class RuleSemantics
{
    public required YaraRule Rule { get; init; }
    public required TargetKind Target { get; init; }
    public Dictionary<string, LoweredString> Strings { get; } = new(StringComparer.Ordinal);
    public List<PatternVariant> AllVariants { get; } = new();
    public List<Diagnostic> Diagnostics { get; } = new();
    public HashSet<string> Features { get; } = new(StringComparer.OrdinalIgnoreCase);
    public bool Approximated { get; set; }
    public bool HasUnusableString { get; set; }

    public PatternVariant? FindVariant(string stringId, string variantId)
        => Strings.TryGetValue(stringId, out var lowered)
            ? lowered.Variants.FirstOrDefault(v => v.VariantId == variantId)
            : null;
}
