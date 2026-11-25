using System.Globalization;
using System.Text;
using YaraTranslator.Parsing;

namespace YaraTranslator.Parsing;

/// <summary>
/// A small, self-contained lexer for the YARA rule language. It is intentionally
/// permissive: unsupported syntax is tokenized so the parser and semantic stages can
/// report it instead of aborting the whole file.
/// </summary>
public sealed class YaraLexer
{
    private readonly string _source;
    private readonly string _file;
    private readonly DiagnosticBag _diagnostics;
    private readonly List<Token> _tokens = new();
    private int _position;
    private int _line = 1;
    private int _column = 1;
    private TokenKind _previous = TokenKind.Unknown;
    private bool _previousWasMatches;

    private YaraLexer(string source, string file, DiagnosticBag diagnostics)
    {
        _source = source;
        _file = file;
        _diagnostics = diagnostics;
    }

    public static IReadOnlyList<Token> Tokenize(string source, string file, DiagnosticBag diagnostics)
    {
        var lexer = new YaraLexer(source, file, diagnostics);
        lexer.Run();
        return lexer._tokens;
    }

    private char Current => _position < _source.Length ? _source[_position] : '\0';

    private char Peek(int offset)
    {
        var index = _position + offset;
        return index < _source.Length ? _source[index] : '\0';
    }

    private bool AtEnd => _position >= _source.Length;

    private char Advance()
    {
        if (AtEnd)
            return '\0';

        var c = _source[_position++];
        if (c == '\r')
        {
            if (_position < _source.Length && _source[_position] == '\n')
                _position++;
            _line++;
            _column = 1;
            return '\n';
        }

        if (c == '\n')
        {
            _line++;
            _column = 1;
            return '\n';
        }

        _column++;
        return c;
    }

    private void Run()
    {
        while (true)
        {
            SkipTrivia();
            if (AtEnd)
                break;

            var line = _line;
            var column = _column;
            var position = _position;
            var c = Current;

            if (c == '"')
            {
                var value = ReadTextString(line, column, position);
                Add(TokenKind.TextString, value.Value, line, column, position, value);
                continue;
            }

            if (c == '/' && (_previous == TokenKind.Equals || _previousWasMatches))
            {
                var value = ReadRegex(line, column, position);
                Add(TokenKind.Regex, value.Pattern, line, column, position, value);
                continue;
            }

            if (c == '{' && _previous == TokenKind.Equals)
            {
                var body = ReadHexString(line, column, position);
                Add(TokenKind.HexString, body, line, column, position, body);
                continue;
            }

            if (c == '$')
            {
                Add(TokenKind.StringIdentifier, ReadIdentifierWithPrefix('$'), line, column, position);
                continue;
            }

            if (c == '#')
            {
                Add(TokenKind.CountIdentifier, ReadIdentifierWithPrefix('#'), line, column, position);
                continue;
            }

            if (c == '@')
            {
                Add(TokenKind.OffsetIdentifier, ReadIdentifierWithPrefix('@'), line, column, position);
                continue;
            }

            if (c == '!')
            {
                if (Peek(1) == '=')
                {
                    Advance();
                    Advance();
                    Add(TokenKind.NotEquals, "!=", line, column, position);
                }
                else if (IsIdentifierStart(Peek(1)))
                {
                    Advance();
                    var text = "!" + ReadIdentifierChars();
                    Add(TokenKind.LengthIdentifier, text, line, column, position);
                }
                else
                {
                    Advance();
                    Add(TokenKind.Bang, "!", line, column, position);
                }

                continue;
            }

            if (IsIdentifierStart(c))
            {
                var text = ReadIdentifierChars();
                Add(TokenKind.Identifier, text, line, column, position);
                continue;
            }

            if (char.IsDigit(c) || (c == '.' && char.IsDigit(Peek(1))))
            {
                ReadNumber(line, column, position);
                continue;
            }

            switch (c)
            {
                case '{':
                    Advance();
                    Add(TokenKind.LBrace, "{", line, column, position);
                    continue;
                case '}':
                    Advance();
                    Add(TokenKind.RBrace, "}", line, column, position);
                    continue;
                case '(':
                    Advance();
                    Add(TokenKind.LParen, "(", line, column, position);
                    continue;
                case ')':
                    Advance();
                    Add(TokenKind.RParen, ")", line, column, position);
                    continue;
                case '[':
                    Advance();
                    Add(TokenKind.LBracket, "[", line, column, position);
                    continue;
                case ']':
                    Advance();
                    Add(TokenKind.RBracket, "]", line, column, position);
                    continue;
                case ':':
                    Advance();
                    Add(TokenKind.Colon, ":", line, column, position);
                    continue;
                case ',':
                    Advance();
                    Add(TokenKind.Comma, ",", line, column, position);
                    continue;
                case '.':
                    if (Peek(1) == '.')
                    {
                        Advance();
                        Advance();
                        Add(TokenKind.DotDot, "..", line, column, position);
                    }
                    else
                    {
                        Advance();
                        Add(TokenKind.Dot, ".", line, column, position);
                    }

                    continue;
                case '=':
                    if (Peek(1) == '=')
                    {
                        Advance();
                        Advance();
                        Add(TokenKind.EqualsEquals, "==", line, column, position);
                    }
                    else
                    {
                        Advance();
                        Add(TokenKind.Equals, "=", line, column, position);
                    }

                    continue;
                case '<':
                    if (Peek(1) == '=')
                    {
                        Advance();
                        Advance();
                        Add(TokenKind.LessOrEqual, "<=", line, column, position);
                    }
                    else if (Peek(1) == '<')
                    {
                        Advance();
                        Advance();
                        Add(TokenKind.ShiftLeft, "<<", line, column, position);
                    }
                    else
                    {
                        Advance();
                        Add(TokenKind.Less, "<", line, column, position);
                    }

                    continue;
                case '>':
                    if (Peek(1) == '=')
                    {
                        Advance();
                        Advance();
                        Add(TokenKind.GreaterOrEqual, ">=", line, column, position);
                    }
                    else if (Peek(1) == '>')
                    {
                        Advance();
                        Advance();
                        Add(TokenKind.ShiftRight, ">>", line, column, position);
                    }
                    else
                    {
                        Advance();
                        Add(TokenKind.Greater, ">", line, column, position);
                    }

                    continue;
                case '+':
                    Advance();
                    Add(TokenKind.Plus, "+", line, column, position);
                    continue;
                case '-':
                    Advance();
                    Add(TokenKind.Minus, "-", line, column, position);
                    continue;
                case '*':
                    Advance();
                    Add(TokenKind.Star, "*", line, column, position);
                    continue;
                case '/':
                    Advance();
                    Add(TokenKind.Slash, "/", line, column, position);
                    continue;
                case '%':
                    Advance();
                    Add(TokenKind.Percent, "%", line, column, position);
                    continue;
                case '\\':
                    Advance();
                    Add(TokenKind.Percent, "\\", line, column, position);
                    continue;
                case '&':
                    Advance();
                    Add(TokenKind.Amp, "&", line, column, position);
                    continue;
                case '|':
                    Advance();
                    Add(TokenKind.Pipe, "|", line, column, position);
                    continue;
                case '^':
                    Advance();
                    Add(TokenKind.Caret, "^", line, column, position);
                    continue;
                case '~':
                    Advance();
                    Add(TokenKind.Tilde, "~", line, column, position);
                    continue;
                case '?':
                    Advance();
                    Add(TokenKind.Question, "?", line, column, position);
                    continue;
                default:
                    Advance();
                    _diagnostics.Warning("YARA1001", $"Unexpected character '{c}'.", new SourceSpan(_file, line, column));
                    Add(TokenKind.Unknown, c.ToString(), line, column, position);
                    continue;
            }
        }

        _tokens.Add(new Token(TokenKind.EndOfFile, string.Empty, _line, _column, _position));
    }

    private void Add(TokenKind kind, string text, int line, int column, int position, object? value = null)
    {
        _tokens.Add(new Token(kind, text, line, column, position, value));
        _previous = kind;
        _previousWasMatches = kind == TokenKind.Identifier &&
                              text.Equals("matches", StringComparison.OrdinalIgnoreCase);
    }

    private void SkipTrivia()
    {
        while (!AtEnd)
        {
            if (char.IsWhiteSpace(Current))
            {
                Advance();
                continue;
            }

            if (Current == '/' && Peek(1) == '/')
            {
                Advance();
                Advance();
                while (!AtEnd && Current != '\n' && Current != '\r')
                    Advance();
                continue;
            }

            if (Current == '/' && Peek(1) == '*')
            {
                var line = _line;
                var column = _column;
                Advance();
                Advance();
                var closed = false;
                while (!AtEnd)
                {
                    if (Current == '*' && Peek(1) == '/')
                    {
                        Advance();
                        Advance();
                        closed = true;
                        break;
                    }

                    Advance();
                }

                if (!closed)
                    _diagnostics.Warning("YARA1002", "Unterminated block comment.", new SourceSpan(_file, line, column));
                continue;
            }

            break;
        }
    }

    private TextLiteral ReadTextString(int line, int column, int position)
    {
        Advance(); // opening quote
        var bytes = new List<byte>();
        var value = new StringBuilder();

        while (!AtEnd)
        {
            var c = Current;
            if (c == '"')
            {
                Advance();
                return new TextLiteral(bytes.ToArray(), value.ToString());
            }

            if (c == '\\')
            {
                Advance();
                if (AtEnd)
                    break;

                var escape = Advance();
                switch (escape)
                {
                    case 'n':
                        bytes.Add(0x0A);
                        value.Append('\n');
                        break;
                    case 'r':
                        bytes.Add(0x0D);
                        value.Append('\r');
                        break;
                    case 't':
                        bytes.Add(0x09);
                        value.Append('\t');
                        break;
                    case '0':
                        bytes.Add(0x00);
                        value.Append('\0');
                        break;
                    case '\\':
                        bytes.Add(0x5C);
                        value.Append('\\');
                        break;
                    case '"':
                        bytes.Add(0x22);
                        value.Append('"');
                        break;
                    case '\'':
                        bytes.Add(0x27);
                        value.Append('\'');
                        break;
                    case 'x':
                    {
                        var hex = ReadHexEscape(2);
                        if (hex is null)
                        {
                            _diagnostics.Warning("YARA1003", "Invalid \\x escape in text string.", new SourceSpan(_file, line, column));
                            bytes.Add((byte)'x');
                            value.Append('x');
                        }
                        else
                        {
                            bytes.Add((byte)hex.Value);
                            value.Append((char)hex.Value);
                        }

                        break;
                    }
                    case 'u':
                    {
                        var hex = ReadHexEscape(4);
                        if (hex is null)
                        {
                            _diagnostics.Warning("YARA1004", "Invalid \\u escape in text string.", new SourceSpan(_file, line, column));
                            bytes.Add((byte)'u');
                            value.Append('u');
                        }
                        else
                        {
                            var ch = (char)hex.Value;
                            bytes.AddRange(Encoding.UTF8.GetBytes(ch.ToString()));
                            value.Append(ch);
                        }

                        break;
                    }
                    default:
                        _diagnostics.Warning("YARA1005", $"Unknown escape '\\{escape}' in text string; treating it literally.", new SourceSpan(_file, line, column));
                        bytes.AddRange(Encoding.UTF8.GetBytes(escape.ToString()));
                        value.Append(escape);
                        break;
                }

                continue;
            }

            Advance();
            bytes.AddRange(Encoding.UTF8.GetBytes(c.ToString()));
            value.Append(c);
        }

        _diagnostics.Error("YARA1006", "Unterminated text string.", new SourceSpan(_file, line, column));
        return new TextLiteral(bytes.ToArray(), value.ToString());
    }

    private int? ReadHexEscape(int digits)
    {
        var value = 0;
        for (var i = 0; i < digits; i++)
        {
            var c = Current;
            var nibble = c switch
            {
                >= '0' and <= '9' => c - '0',
                >= 'a' and <= 'f' => c - 'a' + 10,
                >= 'A' and <= 'F' => c - 'A' + 10,
                _ => -1
            };

            if (nibble < 0)
                return null;

            Advance();
            value = (value << 4) | nibble;
        }

        return value;
    }

    private RegexLiteral ReadRegex(int line, int column, int position)
    {
        Advance(); // opening slash
        var pattern = new StringBuilder();
        var inCharacterClass = false;

        while (!AtEnd)
        {
            var c = Current;
            if (c == '\\')
            {
                pattern.Append(Advance());
                if (!AtEnd)
                    pattern.Append(Advance());
                continue;
            }

            if (c == '[')
            {
                inCharacterClass = true;
                pattern.Append(Advance());
                continue;
            }

            if (c == ']')
            {
                inCharacterClass = false;
                pattern.Append(Advance());
                continue;
            }

            if (c == '/' && !inCharacterClass)
            {
                Advance();
                var flagsStart = _position;
                while (!AtEnd && char.IsLetter(Current))
                    Advance();
                var flags = _source[flagsStart.._position];
                return new RegexLiteral(pattern.ToString(), flags);
            }

            pattern.Append(Advance());
        }

        _diagnostics.Error("YARA1007", "Unterminated regular expression.", new SourceSpan(_file, line, column));
        return new RegexLiteral(pattern.ToString(), string.Empty);
    }

    private string ReadHexString(int line, int column, int position)
    {
        Advance(); // opening brace
        var body = new StringBuilder();

        while (!AtEnd)
        {
            if (Current == '}')
            {
                Advance();
                return body.ToString();
            }

            if (Current == '/' && Peek(1) == '/')
            {
                Advance();
                Advance();
                while (!AtEnd && Current != '\n' && Current != '\r')
                    Advance();
                body.Append(' ');
                continue;
            }

            if (Current == '/' && Peek(1) == '*')
            {
                Advance();
                Advance();
                var closed = false;
                while (!AtEnd)
                {
                    if (Current == '*' && Peek(1) == '/')
                    {
                        Advance();
                        Advance();
                        closed = true;
                        break;
                    }

                    Advance();
                }

                if (!closed)
                    _diagnostics.Warning("YARA1008", "Unterminated block comment in hex string.", new SourceSpan(_file, line, column));
                body.Append(' ');
                continue;
            }

            body.Append(Advance());
        }

        _diagnostics.Error("YARA1009", "Unterminated hex string.", new SourceSpan(_file, line, column));
        return body.ToString();
    }

    private string ReadIdentifierWithPrefix(char prefix)
    {
        Advance(); // prefix
        var text = ReadIdentifierChars();
        if (!AtEnd && Current == '*')
        {
            Advance();
            text += "*";
        }

        return prefix + text;
    }

    private string ReadIdentifierChars()
    {
        var start = _position;
        while (!AtEnd && (char.IsLetterOrDigit(Current) || Current == '_'))
            Advance();
        return _source[start.._position];
    }

    private void ReadNumber(int line, int column, int position)
    {
        var start = _position;
        var isHex = false;
        var isFloat = false;

        if (Current == '0' && (Peek(1) == 'x' || Peek(1) == 'X'))
        {
            isHex = true;
            Advance();
            Advance();
            while (!AtEnd && Uri.IsHexDigit(Current))
                Advance();
        }
        else
        {
            while (!AtEnd && char.IsDigit(Current))
                Advance();

            if (Current == '.' && Peek(1) != '.' && char.IsDigit(Peek(1)))
            {
                isFloat = true;
                Advance();
                while (!AtEnd && char.IsDigit(Current))
                    Advance();
            }

            if ((Current == 'e' || Current == 'E') &&
                (char.IsDigit(Peek(1)) || ((Peek(1) == '+' || Peek(1) == '-') && char.IsDigit(Peek(2)))))
            {
                isFloat = true;
                Advance();
                if (Current == '+' || Current == '-')
                    Advance();
                while (!AtEnd && char.IsDigit(Current))
                    Advance();
            }
        }

        var raw = _source[start.._position];
        long multiplier = 1;
        if (!isHex && !AtEnd && char.IsLetter(Current))
        {
            var suffixStart = _position;
            while (!AtEnd && char.IsLetter(Current))
                Advance();
            var suffix = _source[suffixStart.._position];
            if (suffix.Equals("KB", StringComparison.OrdinalIgnoreCase))
                multiplier = 1024;
            else if (suffix.Equals("MB", StringComparison.OrdinalIgnoreCase))
                multiplier = 1024 * 1024;
            else
                _diagnostics.Warning("YARA1010", $"Unknown numeric suffix '{suffix}'.", new SourceSpan(_file, line, column));
        }

        if (isFloat)
        {
            var text = raw + (multiplier == 1 ? string.Empty : string.Empty);
            if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
            {
                Add(TokenKind.Float, text, line, column, position, d * multiplier);
                return;
            }
        }
        else
        {
            long parsed;
            if (isHex)
            {
                var digits = raw.Length > 2 ? raw[2..] : "0";
                parsed = long.TryParse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var h) ? h : 0;
            }
            else
            {
                parsed = long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) ? l : 0;
            }

            Add(TokenKind.Integer, raw, line, column, position, parsed * multiplier);
            return;
        }

        _diagnostics.Warning("YARA1011", $"Could not parse number '{raw}'.", new SourceSpan(_file, line, column));
        Add(TokenKind.Integer, raw, line, column, position, 0L);
    }

    private static bool IsIdentifierStart(char c) => char.IsLetter(c) || c == '_';
}
