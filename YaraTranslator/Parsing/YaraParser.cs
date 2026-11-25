using System.Globalization;
using YaraTranslator.Parsing;

namespace YaraTranslator.Parsing;

/// <summary>
/// Recursive-descent parser for the YARA 4.x rule language. Unsupported constructs are
/// represented explicitly so later stages can explain exactly what could not be
/// translated instead of failing the whole file.
/// </summary>
public sealed class YaraParser
{
    private static readonly HashSet<string> StringOperators = new(StringComparer.OrdinalIgnoreCase)
    {
        "contains", "icontains", "startswith", "istartswith", "endswith", "iendswith",
        "iequals", "matches"
    };

    private static readonly HashSet<string> SectionNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "meta", "strings", "condition"
    };

    private readonly IReadOnlyList<Token> _tokens;
    private readonly string _file;
    private readonly DiagnosticBag _diagnostics;
    private int _position;

    private YaraParser(IReadOnlyList<Token> tokens, string file, DiagnosticBag diagnostics)
    {
        _tokens = tokens;
        _file = file;
        _diagnostics = diagnostics;
    }

    public static YaraFile Parse(string source, string file, DiagnosticBag diagnostics)
    {
        var lexerTokens = YaraLexer.Tokenize(source, file, diagnostics);
        var parser = new YaraParser(lexerTokens, file, diagnostics);
        return parser.ParseFile();
    }

    private Token Current => _tokens[Math.Min(_position, _tokens.Count - 1)];

    private Token Peek(int offset)
    {
        var index = Math.Min(_position + offset, _tokens.Count - 1);
        return _tokens[index];
    }

    private bool AtEnd => Current.Kind == TokenKind.EndOfFile;

    private Token Advance()
    {
        var token = Current;
        if (!AtEnd)
            _position++;
        return token;
    }

    private bool Check(TokenKind kind) => Current.Kind == kind;

    private bool CheckKeyword(string keyword) =>
        Current.Kind == TokenKind.Identifier &&
        Current.Text.Equals(keyword, StringComparison.OrdinalIgnoreCase);

    private bool Match(TokenKind kind)
    {
        if (!Check(kind))
            return false;
        Advance();
        return true;
    }

    private bool MatchKeyword(string keyword)
    {
        if (!CheckKeyword(keyword))
            return false;
        Advance();
        return true;
    }

    private Token? Expect(TokenKind kind, string description)
    {
        if (Check(kind))
            return Advance();

        _diagnostics.Error("YARA1101", $"Expected {description}, found '{Current.Text}'.", Span(Current));
        return null;
    }

    private bool ExpectKeyword(string keyword)
    {
        if (MatchKeyword(keyword))
            return true;

        _diagnostics.Error("YARA1102", $"Expected '{keyword}', found '{Current.Text}'.", Span(Current));
        return false;
    }

    private SourceSpan Span(Token token) => new(_file, token.Line, token.Column);

    private YaraFile ParseFile()
    {
        var file = new YaraFile { Path = _file };

        while (!AtEnd)
        {
            if (Match(TokenKind.Unknown))
                continue;

            if (CheckKeyword("include"))
            {
                var start = Advance();
                var path = Expect(TokenKind.TextString, "an include path string");
                if (path?.Value is TextLiteral literal)
                    file.Includes.Add(new IncludeDirective(literal.Value, Span(start)));
                else
                    SkipToStatementEnd();
                continue;
            }

            if (CheckKeyword("import"))
            {
                var start = Advance();
                var module = Expect(TokenKind.TextString, "an import module string");
                if (module?.Value is TextLiteral literal)
                    file.Imports.Add(new ImportDirective(literal.Value, Span(start)));
                else
                    SkipToStatementEnd();
                continue;
            }

            var isPrivate = false;
            var isGlobal = false;
            while (CheckKeyword("private") || CheckKeyword("global"))
            {
                if (MatchKeyword("private"))
                    isPrivate = true;
                else if (MatchKeyword("global"))
                    isGlobal = true;
            }

            if (CheckKeyword("rule"))
            {
                var rule = ParseRule(isPrivate, isGlobal);
                if (rule is not null)
                    file.Rules.Add(rule);
                continue;
            }

            _diagnostics.Warning("YARA1103", $"Unexpected top-level token '{Current.Text}'.", Span(Current));
            Advance();
        }

        return file;
    }

    private YaraRule? ParseRule(bool isPrivate, bool isGlobal)
    {
        var ruleToken = Advance(); // rule
        var nameToken = Expect(TokenKind.Identifier, "a rule name");
        if (nameToken is null)
        {
            SkipToRuleEnd();
            return null;
        }

        var rule = new YaraRule
        {
            Name = nameToken.Text,
            File = _file,
            Line = ruleToken.Line,
            Span = Span(ruleToken),
            IsPrivate = isPrivate,
            IsGlobal = isGlobal
        };

        if (Match(TokenKind.Colon))
        {
            while (Check(TokenKind.Identifier) && !Check(TokenKind.LBrace))
                rule.Tags.Add(Advance().Text);
        }

        if (!Match(TokenKind.LBrace))
        {
            _diagnostics.Error("YARA1104", $"Expected '{{' after rule '{rule.Name}'.", Span(Current));
            SkipToRuleEnd();
            return rule;
        }

        ParseRuleBody(rule);

        if (!Match(TokenKind.RBrace))
        {
            _diagnostics.Error("YARA1105", $"Unterminated rule '{rule.Name}'.", rule.Span);
            SkipToRuleEnd();
        }

        return rule;
    }

    private void ParseRuleBody(YaraRule rule)
    {
        while (!AtEnd && !Check(TokenKind.RBrace))
        {
            if (CheckSection("meta"))
            {
                Advance();
                Advance();
                ParseMeta(rule);
                continue;
            }

            if (CheckSection("strings"))
            {
                Advance();
                Advance();
                ParseStrings(rule);
                continue;
            }

            if (CheckSection("condition"))
            {
                Advance();
                Advance();
                if (rule.Condition is not null)
                    _diagnostics.Warning("YARA1106", $"Rule '{rule.Name}' has more than one condition section.", rule.Span);

                rule.Condition = ParseExpression();
                if (!Check(TokenKind.RBrace) && !IsAnySectionStart())
                {
                    _diagnostics.Warning("YARA1107", $"Unexpected token '{Current.Text}' after condition in rule '{rule.Name}'.", Span(Current));
                    SkipToRuleBodyBoundary();
                }

                continue;
            }

            if (Check(TokenKind.Unknown))
            {
                Advance();
                continue;
            }

            _diagnostics.Warning("YARA1108", $"Unexpected token '{Current.Text}' in rule '{rule.Name}'.", Span(Current));
            Advance();
        }
    }

    private void ParseMeta(YaraRule rule)
    {
        while (!AtEnd && !Check(TokenKind.RBrace) && !IsAnySectionStart())
        {
            if (Check(TokenKind.Identifier) && Peek(1).Kind == TokenKind.Equals)
            {
                var name = Advance();
                Advance(); // =
                var value = ParseMetaValue();

                if (value.HasValue)
                    rule.Meta.Add(new MetaEntry(name.Text, value.Value, Span(name)));
                continue;
            }

            _diagnostics.Warning("YARA1109", $"Unexpected token '{Current.Text}' in meta section.", Span(Current));
            Advance();
        }
    }

    private MetaValue ParseMetaValue()
    {
        var negative = false;
        if (Match(TokenKind.Minus))
            negative = true;

        if (Check(TokenKind.TextString))
        {
            var token = Advance();
            var literal = (TextLiteral)token.Value!;
            return new MetaValue(literal.Value);
        }

        if (Check(TokenKind.Integer))
        {
            var token = Advance();
            var value = token.Value is long l ? l : 0;
            return new MetaValue(negative ? -value : value);
        }

        if (Check(TokenKind.Float))
        {
            var token = Advance();
            var value = token.Value is double d ? d : 0;
            return new MetaValue(negative ? -value : value);
        }

        if (CheckKeyword("true"))
        {
            Advance();
            return new MetaValue(true);
        }

        if (CheckKeyword("false"))
        {
            Advance();
            return new MetaValue(false);
        }

        if (Check(TokenKind.Identifier))
        {
            // YARA requires quoted meta strings, but accepting bare identifiers is
            // friendlier than losing the whole rule.
            var token = Advance();
            _diagnostics.Info("YARA1110", $"Meta string '{token.Text}' is not quoted.", Span(token));
            return new MetaValue(token.Text);
        }

        _diagnostics.Warning("YARA1111", $"Unsupported meta value '{Current.Text}'.", Span(Current));
        if (!AtEnd)
            Advance();
        return new MetaValue(null);
    }

    private void ParseStrings(YaraRule rule)
    {
        while (!AtEnd && !Check(TokenKind.RBrace) && !IsAnySectionStart())
        {
            if (!Check(TokenKind.StringIdentifier))
            {
                _diagnostics.Warning("YARA1112", $"Unexpected token '{Current.Text}' in strings section.", Span(Current));
                Advance();
                continue;
            }

            var idToken = Advance();
            if (!Match(TokenKind.Equals))
            {
                _diagnostics.Error("YARA1113", $"Expected '=' after string identifier '{idToken.Text}'.", Span(Current));
                SkipToNextStringOrSection();
                continue;
            }

            YaraString? yaraString = null;
            if (Check(TokenKind.TextString))
            {
                var token = Advance();
                var literal = (TextLiteral)token.Value!;
                yaraString = new YaraString
                {
                    Id = idToken.Text,
                    Kind = YaraStringKind.Text,
                    TextBytes = literal.Bytes,
                    TextValue = literal.Value,
                    Span = Span(idToken)
                };
            }
            else if (Check(TokenKind.HexString))
            {
                var token = Advance();
                yaraString = new YaraString
                {
                    Id = idToken.Text,
                    Kind = YaraStringKind.Hex,
                    HexBody = token.Value as string ?? token.Text,
                    Span = Span(idToken)
                };
            }
            else if (Check(TokenKind.Regex))
            {
                var token = Advance();
                var literal = token.Value as RegexLiteral ?? new RegexLiteral(token.Text, string.Empty);
                yaraString = new YaraString
                {
                    Id = idToken.Text,
                    Kind = YaraStringKind.Regex,
                    RegexPattern = literal.Pattern,
                    RegexFlags = literal.Flags,
                    Span = Span(idToken)
                };
            }
            else
            {
                _diagnostics.Error("YARA1114", $"Unsupported string value for '{idToken.Text}'.", Span(Current));
                SkipToNextStringOrSection();
                continue;
            }

            ParseStringModifiers(yaraString);
            rule.Strings.Add(yaraString);
        }
    }

    private void ParseStringModifiers(YaraString yaraString)
    {
        while (Check(TokenKind.Identifier))
        {
            var modifier = Current.Text;
            var lower = modifier.ToLowerInvariant();

            if (lower is "ascii" or "wide" or "nocase" or "fullword" or "private" or "base64" or "base64wide")
            {
                Advance();
                switch (lower)
                {
                    case "ascii":
                        yaraString.Ascii = true;
                        break;
                    case "wide":
                        yaraString.Wide = true;
                        break;
                    case "nocase":
                        yaraString.NoCase = true;
                        break;
                    case "fullword":
                        yaraString.FullWord = true;
                        break;
                    case "private":
                        yaraString.Private = true;
                        break;
                    case "base64":
                        yaraString.Base64 = true;
                        break;
                    case "base64wide":
                        yaraString.Base64Wide = true;
                        break;
                }

                continue;
            }

            if (lower == "xor")
            {
                Advance();
                yaraString.XorMin = 0;
                yaraString.XorMax = 255;

                if (Match(TokenKind.LParen))
                {
                    var min = ParseModifierInteger();
                    var max = min;
                    if (Match(TokenKind.Minus))
                        max = ParseModifierInteger();

                    yaraString.XorMin = min;
                    yaraString.XorMax = max;

                    if (!Match(TokenKind.RParen))
                        _diagnostics.Warning("YARA1115", "Expected ')' after xor modifier.", Span(Current));
                }

                continue;
            }

            // A section keyword or a known condition token means the modifier list is over.
            if (SectionNames.Contains(lower) || lower is "and" or "or" or "not")
                return;

            _diagnostics.Warning("YARA1116", $"Unknown string modifier '{modifier}'.", Span(Current));
            Advance();
        }
    }

    private int ParseModifierInteger()
    {
        if (Check(TokenKind.Integer))
        {
            var value = Advance().Value is long l ? l : 0;
            return (int)Math.Clamp(value, 0, 255);
        }

        _diagnostics.Warning("YARA1117", "Expected an integer in xor modifier.", Span(Current));
        return 0;
    }

    private ConditionExpr ParseExpression() => ParseOr();

    private ConditionExpr ParseOr()
    {
        var first = ParseAnd();
        if (!MatchKeyword("or"))
            return first;

        var items = new List<ConditionExpr> { first };
        do
        {
            items.Add(ParseAnd());
        } while (MatchKeyword("or"));

        return new OrExpr(items, first.Span);
    }

    private ConditionExpr ParseAnd()
    {
        var first = ParseNot();
        if (!MatchKeyword("and"))
            return first;

        var items = new List<ConditionExpr> { first };
        do
        {
            items.Add(ParseNot());
        } while (MatchKeyword("and"));

        return new AndExpr(items, first.Span);
    }

    private ConditionExpr ParseNot()
    {
        if (CheckKeyword("not"))
        {
            var token = Advance();
            return new NotExpr(ParseNot(), Span(token));
        }

        return ParseComparison();
    }

    private ConditionExpr ParseComparison()
    {
        var left = ParseBitOr();

        while (true)
        {
            if (Current.Kind is TokenKind.EqualsEquals or TokenKind.NotEquals or
                TokenKind.Less or TokenKind.LessOrEqual or
                TokenKind.Greater or TokenKind.GreaterOrEqual)
            {
                var op = Advance();
                var right = ParseBitOr();
                left = new BinaryExpr(op.Text, left, right, left.Span);
                continue;
            }

            if (Current.Kind == TokenKind.Identifier)
            {
                var keyword = Current.Text;
                var lower = keyword.ToLowerInvariant();
                if (lower is "at" or "in")
                {
                    Advance();
                    var right = ParseAdditive();
                    left = new BinaryExpr(lower, left, right, left.Span);
                    continue;
                }

                if (StringOperators.Contains(lower))
                {
                    Advance();
                    var right = ParseBitOr();
                    left = new BinaryExpr(lower, left, right, left.Span);
                    continue;
                }
            }

            break;
        }

        return left;
    }

    private ConditionExpr ParseBitOr()
    {
        var left = ParseBitXor();
        while (Match(TokenKind.Pipe))
            left = new BinaryExpr("|", left, ParseBitXor(), left.Span);
        return left;
    }

    private ConditionExpr ParseBitXor()
    {
        var left = ParseBitAnd();
        while (Match(TokenKind.Caret))
            left = new BinaryExpr("^", left, ParseBitAnd(), left.Span);
        return left;
    }

    private ConditionExpr ParseBitAnd()
    {
        var left = ParseShift();
        while (Match(TokenKind.Amp))
            left = new BinaryExpr("&", left, ParseShift(), left.Span);
        return left;
    }

    private ConditionExpr ParseShift()
    {
        var left = ParseAdditive();
        while (Current.Kind is TokenKind.ShiftLeft or TokenKind.ShiftRight)
        {
            var op = Advance();
            left = new BinaryExpr(op.Text, left, ParseAdditive(), left.Span);
        }

        return left;
    }

    private ConditionExpr ParseAdditive()
    {
        var left = ParseMultiplicative();
        while (Current.Kind is TokenKind.Plus or TokenKind.Minus or TokenKind.DotDot)
        {
            var op = Advance();
            left = new BinaryExpr(op.Kind == TokenKind.DotDot ? ".." : op.Text, left, ParseMultiplicative(), left.Span);
        }

        return left;
    }

    private ConditionExpr ParseMultiplicative()
    {
        var left = ParseUnary();
        while (Current.Kind is TokenKind.Star or TokenKind.Slash or TokenKind.Percent)
        {
            var op = Advance();
            left = new BinaryExpr(op.Kind == TokenKind.Percent ? "%" : op.Text, left, ParseUnary(), left.Span);
        }

        return left;
    }

    private ConditionExpr ParseUnary()
    {
        if (Current.Kind is TokenKind.Minus or TokenKind.Tilde or TokenKind.Bang)
        {
            var op = Advance();
            return new UnaryExpr(op.Text, ParseUnary(), Span(op));
        }

        if (CheckKeyword("defined"))
        {
            var token = Advance();
            return new UnaryExpr("defined", ParseUnary(), Span(token));
        }

        return ParsePrimary();
    }

    private ConditionExpr ParsePrimary()
    {
        if (AtEnd || Check(TokenKind.RBrace) || IsAnySectionStart())
        {
            _diagnostics.Warning("YARA1128", "Expected an expression.", Span(Current));
            return new UnsupportedExpr("Expected an expression.", Span(Current));
        }

        if (Match(TokenKind.LParen))
        {
            var expr = ParseExpression();
            if (!Match(TokenKind.RParen))
                _diagnostics.Warning("YARA1118", "Expected ')' in expression.", Span(Current));
            return expr;
        }

        if (Check(TokenKind.Integer))
        {
            var token = Advance();
            var value = token.Value is long l ? l : 0;

            if (CheckKeyword("of"))
                return ParseOf(new OfQuantifier(OfQuantifierKind.Number, value), token);

            return new IntLiteralExpr(value, Span(token));
        }

        if (Check(TokenKind.Float))
        {
            var token = Advance();
            return new FloatLiteralExpr(token.Value is double d ? d : 0, Span(token));
        }

        if (Check(TokenKind.TextString))
        {
            var token = Advance();
            return new TextLiteralExpr(((TextLiteral)token.Value!).Value, Span(token));
        }

        if (Check(TokenKind.Regex))
        {
            var token = Advance();
            var regex = (RegexLiteral)token.Value!;
            return new RegexLiteralExpr(regex.Pattern, regex.Flags, Span(token));
        }

        if (Check(TokenKind.StringIdentifier))
        {
            var token = Advance();
            return new StringMatchExpr(token.Text, Span(token));
        }

        if (Check(TokenKind.CountIdentifier))
        {
            var token = Advance();
            return new StringCountExpr(token.Text, Span(token));
        }

        if (Check(TokenKind.OffsetIdentifier))
        {
            var token = Advance();
            var index = ParseOptionalIndex();
            return new StringOffsetExpr(token.Text, index, Span(token));
        }

        if (Check(TokenKind.LengthIdentifier))
        {
            var token = Advance();
            var index = ParseOptionalIndex();
            return new StringLengthExpr(token.Text, index, Span(token));
        }

        if (Check(TokenKind.Identifier))
        {
            var token = Current;
            var lower = token.Text.ToLowerInvariant();

            if (lower is "true" or "false")
            {
                Advance();
                return new BoolLiteralExpr(lower == "true", Span(token));
            }

            if (lower is "all" or "any" or "none")
            {
                Advance();
                if (CheckKeyword("of"))
                {
                    var quantifier = lower switch
                    {
                        "all" => OfQuantifierKind.All,
                        "any" => OfQuantifierKind.Any,
                        _ => OfQuantifierKind.None
                    };
                    return ParseOf(new OfQuantifier(quantifier), token);
                }

                return new IdentifierExpr(token.Text, Span(token));
            }

            if (lower == "for")
                return ParseFor();

            Advance();
            ConditionExpr expr = new IdentifierExpr(token.Text, Span(token));

            while (true)
            {
                if (Match(TokenKind.Dot))
                {
                    var member = Expect(TokenKind.Identifier, "a member name");
                    if (member is null)
                        break;
                    expr = new MemberExpr(expr, member.Text, Span(token));
                    continue;
                }

                if (Check(TokenKind.LParen))
                {
                    var args = ParseArguments();
                    var name = FlattenName(expr) ?? token.Text;
                    expr = new FunctionCallExpr(name, args, Span(token));
                    continue;
                }

                if (Check(TokenKind.LBracket))
                {
                    Advance();
                    var index = ParseExpression();
                    if (!Match(TokenKind.RBracket))
                        _diagnostics.Warning("YARA1119", "Expected ']' in index expression.", Span(Current));
                    expr = new IndexExpr(expr, index, Span(token));
                    continue;
                }

                break;
            }

            return expr;
        }

        var unexpected = Current;
        _diagnostics.Warning("YARA1120", $"Unexpected token '{unexpected.Text}' in expression.", Span(unexpected));
        if (!AtEnd)
            Advance();
        return new UnsupportedExpr($"Unexpected token '{unexpected.Text}'.", Span(unexpected));
    }

    private ConditionExpr? ParseOptionalIndex()
    {
        if (!Match(TokenKind.LBracket))
            return null;

        var index = ParseExpression();
        if (!Match(TokenKind.RBracket))
            _diagnostics.Warning("YARA1121", "Expected ']' in string index.", Span(Current));
        return index;
    }

    private IReadOnlyList<ConditionExpr> ParseArguments()
    {
        var args = new List<ConditionExpr>();
        if (!Match(TokenKind.LParen))
            return args;

        if (!Check(TokenKind.RParen))
        {
            do
            {
                args.Add(ParseExpression());
            } while (Match(TokenKind.Comma));
        }

        if (!Match(TokenKind.RParen))
            _diagnostics.Warning("YARA1122", "Expected ')' after arguments.", Span(Current));

        return args;
    }

    private ConditionExpr ParseOf(OfQuantifier quantifier, Token start)
    {
        if (!ExpectKeyword("of"))
            return new UnsupportedExpr("Malformed 'of' expression.", Span(start));

        StringSet set;
        if (MatchKeyword("them"))
        {
            set = new AllStringsSet();
        }
        else if (Match(TokenKind.LParen))
        {
            var patterns = new List<string>();
            if (!Check(TokenKind.RParen))
            {
                do
                {
                    if (Check(TokenKind.StringIdentifier))
                        patterns.Add(Advance().Text);
                    else if (Check(TokenKind.Identifier) && Current.Text.Equals("them", StringComparison.OrdinalIgnoreCase))
                    {
                        Advance();
                        patterns.Add("them");
                    }
                    else
                    {
                        _diagnostics.Warning("YARA1123", $"Expected a string identifier in 'of' set, found '{Current.Text}'.", Span(Current));
                        if (!AtEnd)
                            Advance();
                    }
                } while (Match(TokenKind.Comma));
            }

            if (!Match(TokenKind.RParen))
                _diagnostics.Warning("YARA1124", "Expected ')' after 'of' set.", Span(Current));

            set = new NamedStringsSet(patterns);
        }
        else
        {
            _diagnostics.Warning("YARA1125", "Expected 'them' or a parenthesized string set after 'of'.", Span(Current));
            set = new NamedStringsSet(Array.Empty<string>());
        }

        string? rangeKind = null;
        ConditionExpr? rangeExpression = null;
        if (CheckKeyword("at") || CheckKeyword("in"))
        {
            rangeKind = Advance().Text.ToLowerInvariant();
            rangeExpression = ParseAdditive();
        }

        return new OfExpr(quantifier, set, rangeKind, rangeExpression, Span(start));
    }

    private ConditionExpr ParseFor()
    {
        var start = Advance(); // for
        var depth = 0;

        while (!AtEnd && !Check(TokenKind.RBrace))
        {
            if (Current.Kind == TokenKind.Colon && depth == 0)
                break;

            if (Current.Kind == TokenKind.LParen)
                depth++;
            else if (Current.Kind == TokenKind.RParen && depth > 0)
                depth--;

            Advance();
        }

        if (!Match(TokenKind.Colon))
        {
            _diagnostics.Warning("YARA1126", "Malformed 'for' expression.", Span(start));
            return new UnsupportedExpr("Malformed 'for' expression.", Span(start));
        }

        ConditionExpr? body = null;
        if (Match(TokenKind.LParen))
        {
            body = ParseExpression();
            if (!Match(TokenKind.RParen))
                _diagnostics.Warning("YARA1127", "Expected ')' after 'for' body.", Span(Current));
        }
        else
        {
            body = ParseExpression();
        }

        return new ForExpr(body, null, Span(start));
    }

    private static string? FlattenName(ConditionExpr expr)
    {
        return expr switch
        {
            IdentifierExpr identifier => identifier.Name,
            MemberExpr member => FlattenName(member.Target) is { } target
                ? $"{target}.{member.Member}"
                : member.Member,
            _ => null
        };
    }

    private bool CheckSection(string name) => CheckKeyword(name) && Peek(1).Kind == TokenKind.Colon;

    private bool IsAnySectionStart()
        => SectionNames.Any(CheckSection);

    private void SkipToStatementEnd()
    {
        while (!AtEnd && !Check(TokenKind.RBrace) && !CheckKeyword("rule") && !CheckKeyword("include") && !CheckKeyword("import"))
            Advance();
    }

    private void SkipToNextStringOrSection()
    {
        while (!AtEnd && !Check(TokenKind.RBrace) && !IsAnySectionStart() && !Check(TokenKind.StringIdentifier))
            Advance();
    }

    private void SkipToRuleBodyBoundary()
    {
        while (!AtEnd && !Check(TokenKind.RBrace) && !IsAnySectionStart())
            Advance();
    }

    private void SkipToRuleEnd()
    {
        var depth = 0;
        while (!AtEnd)
        {
            if (Check(TokenKind.LBrace))
            {
                depth++;
                Advance();
                continue;
            }

            if (Check(TokenKind.RBrace))
            {
                depth--;
                Advance();
                if (depth <= 0)
                    return;
                continue;
            }

            Advance();
        }
    }

    private readonly record struct MetaValue(object? Value)
    {
        public bool HasValue => Value is not null;
    }
}
