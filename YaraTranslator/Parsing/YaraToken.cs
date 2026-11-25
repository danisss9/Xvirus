namespace YaraTranslator.Parsing;

public enum TokenKind
{
    EndOfFile,
    Identifier,
    StringIdentifier,
    CountIdentifier,
    OffsetIdentifier,
    LengthIdentifier,
    Integer,
    Float,
    TextString,
    HexString,
    Regex,
    LBrace,
    RBrace,
    LParen,
    RParen,
    LBracket,
    RBracket,
    Colon,
    Comma,
    Dot,
    DotDot,
    Equals,
    EqualsEquals,
    NotEquals,
    Less,
    LessOrEqual,
    Greater,
    GreaterOrEqual,
    Plus,
    Minus,
    Star,
    Slash,
    Percent,
    Amp,
    Pipe,
    Caret,
    Tilde,
    Bang,
    ShiftLeft,
    ShiftRight,
    Question,
    Unknown
}

public sealed record TextLiteral(byte[] Bytes, string Value);

public sealed record RegexLiteral(string Pattern, string Flags);

public sealed record Token(
    TokenKind Kind,
    string Text,
    int Line,
    int Column,
    int Position,
    object? Value = null)
{
    public override string ToString() => $"{Kind} '{Text}' ({Line}:{Column})";
}
