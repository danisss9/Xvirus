using YaraTranslator.Parsing;

namespace YaraTranslator.Parsing;

public sealed class YaraFile
{
    public string Path { get; init; } = string.Empty;
    public List<YaraRule> Rules { get; } = new();
    public List<IncludeDirective> Includes { get; } = new();
    public List<ImportDirective> Imports { get; } = new();
}

public sealed record IncludeDirective(string Path, SourceSpan Span);

public sealed record ImportDirective(string Module, SourceSpan Span);

public sealed class YaraRule
{
    public string Name { get; init; } = string.Empty;
    public string File { get; init; } = string.Empty;
    public int Line { get; init; }
    public bool IsPrivate { get; set; }
    public bool IsGlobal { get; set; }
    public List<string> Tags { get; } = new();
    public List<MetaEntry> Meta { get; } = new();
    public List<YaraString> Strings { get; } = new();
    public ConditionExpr? Condition { get; set; }
    public SourceSpan Span { get; init; } = new(string.Empty, 0, 0);

    public MetaEntry? FindMeta(string name)
        => Meta.FirstOrDefault(m => m.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
}

public sealed record MetaEntry(string Name, object? Value, SourceSpan Span);

public enum YaraStringKind
{
    Text,
    Hex,
    Regex
}

public sealed class YaraString
{
    public string Id { get; init; } = string.Empty;
    public YaraStringKind Kind { get; init; }
    public byte[] TextBytes { get; init; } = Array.Empty<byte>();
    public string TextValue { get; init; } = string.Empty;
    public string HexBody { get; init; } = string.Empty;
    public string RegexPattern { get; init; } = string.Empty;
    public string RegexFlags { get; init; } = string.Empty;
    public bool Ascii { get; set; }
    public bool Wide { get; set; }
    public bool NoCase { get; set; }
    public bool FullWord { get; set; }
    public bool Private { get; set; }
    public bool Base64 { get; set; }
    public bool Base64Wide { get; set; }
    public int XorMin { get; set; } = -1;
    public int XorMax { get; set; } = -1;
    public SourceSpan Span { get; init; } = new(string.Empty, 0, 0);

    public bool HasXor => XorMin >= 0;
}

public abstract record ConditionExpr(SourceSpan Span);

public sealed record BoolLiteralExpr(bool Value, SourceSpan Span) : ConditionExpr(Span);

public sealed record IntLiteralExpr(long Value, SourceSpan Span) : ConditionExpr(Span);

public sealed record FloatLiteralExpr(double Value, SourceSpan Span) : ConditionExpr(Span);

public sealed record TextLiteralExpr(string Value, SourceSpan Span) : ConditionExpr(Span);

public sealed record RegexLiteralExpr(string Pattern, string Flags, SourceSpan Span) : ConditionExpr(Span);

public sealed record IdentifierExpr(string Name, SourceSpan Span) : ConditionExpr(Span);

public sealed record StringMatchExpr(string Id, SourceSpan Span) : ConditionExpr(Span);

public sealed record StringCountExpr(string Id, SourceSpan Span) : ConditionExpr(Span);

public sealed record StringOffsetExpr(string Id, ConditionExpr? Index, SourceSpan Span) : ConditionExpr(Span);

public sealed record StringLengthExpr(string Id, ConditionExpr? Index, SourceSpan Span) : ConditionExpr(Span);

public sealed record UnaryExpr(string Operator, ConditionExpr Operand, SourceSpan Span) : ConditionExpr(Span);

public sealed record BinaryExpr(string Operator, ConditionExpr Left, ConditionExpr Right, SourceSpan Span) : ConditionExpr(Span);

public sealed record AndExpr(IReadOnlyList<ConditionExpr> Items, SourceSpan Span) : ConditionExpr(Span);

public sealed record OrExpr(IReadOnlyList<ConditionExpr> Items, SourceSpan Span) : ConditionExpr(Span);

public sealed record NotExpr(ConditionExpr Inner, SourceSpan Span) : ConditionExpr(Span);

public sealed record FunctionCallExpr(string Name, IReadOnlyList<ConditionExpr> Arguments, SourceSpan Span) : ConditionExpr(Span);

public sealed record MemberExpr(ConditionExpr Target, string Member, SourceSpan Span) : ConditionExpr(Span);

public sealed record IndexExpr(ConditionExpr Target, ConditionExpr Index, SourceSpan Span) : ConditionExpr(Span);

public sealed record UnsupportedExpr(string Reason, SourceSpan Span) : ConditionExpr(Span);

public enum OfQuantifierKind
{
    All,
    Any,
    None,
    Number
}

public sealed record OfQuantifier(OfQuantifierKind Kind, long Number = 0);

public abstract record StringSet;

public sealed record AllStringsSet : StringSet;

public sealed record NamedStringsSet(IReadOnlyList<string> Patterns) : StringSet;

public sealed record OfExpr(
    OfQuantifier Quantifier,
    StringSet Set,
    string? RangeKind,
    ConditionExpr? RangeExpression,
    SourceSpan Span) : ConditionExpr(Span);

public sealed record ForExpr(
    ConditionExpr? Body,
    ConditionExpr? Range,
    SourceSpan Span) : ConditionExpr(Span);
