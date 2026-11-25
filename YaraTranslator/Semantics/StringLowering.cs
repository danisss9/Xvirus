using System.Text;
using YaraTranslator.Parsing;

namespace YaraTranslator.Semantics;

public sealed record PatternVariant(
    string StringId,
    string VariantId,
    string Hex,
    int ByteLength,
    string Description);

public sealed class LoweredString
{
    public required YaraString Source { get; init; }
    public List<PatternVariant> Variants { get; } = new();
    public List<Diagnostic> Diagnostics { get; } = new();
    public HashSet<string> Features { get; } = new(StringComparer.OrdinalIgnoreCase);
    public bool Approximated { get; set; }
    public bool Unusable { get; set; }
}

/// <summary>
/// Turns YARA string declarations into literal byte patterns. Every expansion is capped
/// and any lossy conversion is marked as an approximation instead of being hidden.
/// </summary>
public sealed class StringLowering
{
    private readonly TranslationOptions _options;

    public StringLowering(TranslationOptions options)
    {
        _options = options;
    }

    public LoweredString Lower(YaraString source, string ruleName)
    {
        var result = new LoweredString { Source = source };
        result.Features.Add(source.Kind.ToString().ToLowerInvariant());

        if (source.Base64 || source.Base64Wide)
        {
            result.Unusable = true;
            result.Diagnostics.Add(new Diagnostic(
                "YARA2001",
                DiagnosticSeverity.Warning,
                "base64/base64wide string modifiers are not supported by this translator.",
                source.Span,
                ruleName));
            return result;
        }

        if (source.FullWord && !_options.AllowApproximations)
        {
            result.Unusable = true;
            result.Diagnostics.Add(new Diagnostic(
                "YARA2002",
                DiagnosticSeverity.Warning,
                "fullword is not representable by the heuristic engine; use --allow-approximations to emit the bare literal.",
                source.Span,
                ruleName));
            return result;
        }

        if (source.FullWord)
        {
            result.Approximated = true;
            result.Features.Add("fullword-approximation");
            result.Diagnostics.Add(new Diagnostic(
                "YARA2003",
                DiagnosticSeverity.Warning,
                "fullword boundary checks are ignored; the literal may match inside larger words.",
                source.Span,
                ruleName));
        }

        switch (source.Kind)
        {
            case YaraStringKind.Text:
                LowerText(source, result, ruleName);
                break;
            case YaraStringKind.Hex:
                LowerHex(source, result, ruleName);
                break;
            case YaraStringKind.Regex:
                LowerRegex(source, result, ruleName);
                break;
        }

        if (source.NoCase)
            result.Features.Add("nocase");
        if (source.Ascii)
            result.Features.Add("ascii");
        if (source.Wide)
            result.Features.Add("wide");
        if (source.HasXor)
            result.Features.Add("xor");

        if (result.Variants.Count == 0)
            result.Unusable = true;

        // Renumber variants after deduplication so IDs are stable.
        var renumbered = result.Variants
            .Select((variant, index) => variant with { VariantId = $"{source.Id}#{index}" })
            .ToList();
        result.Variants.Clear();
        result.Variants.AddRange(renumbered);

        return result;
    }

    private void LowerText(YaraString source, LoweredString result, string ruleName)
    {
        var includeAscii = source.Ascii || (!source.Ascii && !source.Wide);
        var includeWide = source.Wide;

        if (includeAscii)
        {
            AddEncodedVariants(source, result, source.TextBytes, "ascii", ruleName);
        }

        if (includeWide)
        {
            var wide = ToWide(source.TextValue);
            AddEncodedVariants(source, result, wide, "wide", ruleName);
        }
    }

    private void LowerHex(YaraString source, LoweredString result, string ruleName)
    {
        if (source.NoCase)
        {
            result.Diagnostics.Add(new Diagnostic(
                "YARA2004",
                DiagnosticSeverity.Warning,
                "nocase has no effect on a hex string and was ignored.",
                source.Span,
                ruleName));
        }

        HexExpansionResult expansion;
        try
        {
            expansion = new HexPatternExpander(_options.MaxVariantsPerString, _options.MaxPatternBytes)
                .Expand(source.HexBody);
        }
        catch (ExpansionLimitException ex)
        {
            result.Unusable = true;
            result.Approximated = true;
            result.Diagnostics.Add(new Diagnostic(
                "YARA2005",
                DiagnosticSeverity.Warning,
                $"Hex string could not be fully expanded: {ex.Message}",
                source.Span,
                ruleName));
            return;
        }
        catch (Exception ex)
        {
            result.Unusable = true;
            result.Diagnostics.Add(new Diagnostic(
                "YARA2006",
                DiagnosticSeverity.Warning,
                $"Invalid hex string: {ex.Message}",
                source.Span,
                ruleName));
            return;
        }

        if (expansion.HasWildcard)
            result.Features.Add("hex-wildcard");
        if (expansion.HasJump)
            result.Features.Add("hex-jump");
        if (expansion.HasAlternative)
            result.Features.Add("hex-alternative");
        if (expansion.HasNegation)
            result.Features.Add("hex-negation");

        var variants = expansion.Variants;
        if (source.HasXor)
            variants = ExpandXor(variants, source, result, ruleName);

        AddVariants(source, result, variants, "hex", ruleName);
    }

    private void LowerRegex(YaraString source, LoweredString result, string ruleName)
    {
        result.Features.Add("regex");
        if (!_options.AllowApproximations)
        {
            result.Unusable = true;
            result.Diagnostics.Add(new Diagnostic(
                "YARA2007",
                DiagnosticSeverity.Warning,
                "Regular expressions are not representable by the heuristic engine.",
                source.Span,
                ruleName));
            return;
        }

        var literal = RegexLiteralExtractor.LongestLiteral(source.RegexPattern, _options.MinPatternBytes);
        if (literal is null)
        {
            result.Unusable = true;
            result.Diagnostics.Add(new Diagnostic(
                "YARA2008",
                DiagnosticSeverity.Warning,
                "No literal run long enough to approximate the regular expression was found.",
                source.Span,
                ruleName));
            return;
        }

        result.Approximated = true;
        result.Features.Add("regex-literal");
        result.Diagnostics.Add(new Diagnostic(
            "YARA2009",
            DiagnosticSeverity.Warning,
            $"Regular expression approximated by its longest literal run '{literal}'.",
            source.Span,
            ruleName));

        var nocase = source.NoCase || source.RegexFlags.Contains('i');
        var bytes = Encoding.UTF8.GetBytes(literal);
        AddEncodedVariants(source, result, bytes, "regex-literal", ruleName, nocase);
    }

    private void AddEncodedVariants(
        YaraString source,
        LoweredString result,
        byte[] bytes,
        string description,
        string ruleName,
        bool? forceNoCase = null)
    {
        var variants = new List<byte[]> { bytes };

        if (forceNoCase ?? source.NoCase)
        {
            try
            {
                variants = ExpandNoCase(variants, _options.MaxVariantsPerString);
            }
            catch (ExpansionLimitException ex)
            {
                result.Unusable = true;
                result.Approximated = true;
                result.Diagnostics.Add(new Diagnostic(
                    "YARA2010",
                    DiagnosticSeverity.Warning,
                    $"nocase expansion failed: {ex.Message}",
                    source.Span,
                    ruleName));
                return;
            }
        }

        if (source.HasXor)
            variants = ExpandXor(variants, source, result, ruleName);

        AddVariants(source, result, variants, description, ruleName);
    }

    private List<byte[]> ExpandNoCase(List<byte[]> variants, int maxVariants)
    {
        if (variants.Count == 0 || variants[0].Length == 0)
            return variants;

        var current = variants;
        for (var index = 0; index < current[0].Length; index++)
        {
            var anyLetter = current.Any(v => IsAsciiLetter(v[index]));
            if (!anyLetter)
                continue;

            if ((long)current.Count * 2 > maxVariants)
                throw new ExpansionLimitException($"case variants exceed {maxVariants}.");

            var next = new List<byte[]>(current.Count * 2);
            foreach (var variant in current)
            {
                var b = variant[index];
                if (!IsAsciiLetter(b))
                {
                    next.Add(variant);
                    continue;
                }

                var lower = (byte)char.ToLowerInvariant((char)b);
                var upper = (byte)char.ToUpperInvariant((char)b);
                var lowerVariant = (byte[])variant.Clone();
                lowerVariant[index] = lower;
                next.Add(lowerVariant);

                var upperVariant = (byte[])variant.Clone();
                upperVariant[index] = upper;
                next.Add(upperVariant);
            }

            current = next;
        }

        return current;
    }

    private List<byte[]> ExpandXor(List<byte[]> variants, YaraString source, LoweredString result, string ruleName)
    {
        var min = Math.Clamp(source.XorMin, 0, 255);
        var max = Math.Clamp(source.XorMax, 0, 255);
        if (max < min)
            (min, max) = (max, min);

        var keyCount = max - min + 1;
        if ((long)variants.Count * keyCount > _options.MaxVariantsPerString)
        {
            result.Unusable = true;
            result.Approximated = true;
            result.Diagnostics.Add(new Diagnostic(
                "YARA2011",
                DiagnosticSeverity.Warning,
                $"xor expansion exceeds {_options.MaxVariantsPerString} variants.",
                source.Span,
                ruleName));
            return new List<byte[]>();
        }

        var expanded = new List<byte[]>(variants.Count * keyCount);
        foreach (var variant in variants)
        {
            for (var key = min; key <= max; key++)
            {
                var copy = new byte[variant.Length];
                for (var i = 0; i < variant.Length; i++)
                    copy[i] = (byte)(variant[i] ^ key);
                expanded.Add(copy);
            }
        }

        return expanded;
    }

    private void AddVariants(
        YaraString source,
        LoweredString result,
        IEnumerable<byte[]> variants,
        string description,
        string ruleName)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var bytes in variants)
        {
            if (bytes.Length < _options.MinPatternBytes)
            {
                result.Approximated = true;
                result.Diagnostics.Add(new Diagnostic(
                    "YARA2012",
                    DiagnosticSeverity.Warning,
                    $"Pattern '{source.Id}' is only {bytes.Length} bytes long; ignoring this variant (minimum is {_options.MinPatternBytes}).",
                    source.Span,
                    ruleName));
                continue;
            }

            if (bytes.Length > _options.MaxPatternBytes)
            {
                result.Unusable = true;
                result.Diagnostics.Add(new Diagnostic(
                    "YARA2013",
                    DiagnosticSeverity.Warning,
                    $"Pattern '{source.Id}' is {bytes.Length} bytes long; maximum is {_options.MaxPatternBytes}.",
                    source.Span,
                    ruleName));
                return;
            }

            var hex = Convert.ToHexString(bytes);
            if (!seen.Add(hex))
                continue;

            if (result.Variants.Count >= _options.MaxVariantsPerString)
            {
                result.Unusable = true;
                result.Approximated = true;
                result.Diagnostics.Add(new Diagnostic(
                    "YARA2014",
                    DiagnosticSeverity.Warning,
                    $"Pattern '{source.Id}' exceeds the {_options.MaxVariantsPerString}-variant limit.",
                    source.Span,
                    ruleName));
                return;
            }

            result.Variants.Add(new PatternVariant(source.Id, $"{source.Id}#{result.Variants.Count}", hex, bytes.Length, description));
        }
    }

    private static byte[] ToWide(string text)
    {
        if (text.All(c => c <= 0x7F))
        {
            var bytes = new byte[text.Length * 2];
            for (var i = 0; i < text.Length; i++)
            {
                bytes[i * 2] = (byte)text[i];
                bytes[i * 2 + 1] = 0;
            }

            return bytes;
        }

        return Encoding.Unicode.GetBytes(text);
    }

    private static bool IsAsciiLetter(byte value)
        => value is >= (byte)'A' and <= (byte)'Z' or >= (byte)'a' and <= (byte)'z';
}

internal static class RegexLiteralExtractor
{
    public static string? LongestLiteral(string pattern, int minimumLength)
    {
        var best = string.Empty;
        var current = new StringBuilder();

        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            if (c == '\\')
            {
                if (i + 1 >= pattern.Length)
                    break;

                var escaped = pattern[++i];
                if (IsLiteralEscape(escaped))
                {
                    current.Append(escaped);
                }
                else
                {
                    Flush();
                }

                continue;
            }

            if (IsRegexMetacharacter(c))
            {
                Flush();
                continue;
            }

            current.Append(c);
        }

        Flush();
        return best.Length >= minimumLength ? best : null;

        void Flush()
        {
            if (current.Length > best.Length)
                best = current.ToString();
            current.Clear();
        }
    }

    private static bool IsLiteralEscape(char c) =>
        c is '.' or '\\' or '+' or '*' or '?' or '[' or ']' or '(' or ')' or
            '{' or '}' or '|' or '^' or '$' or '/' or '-';

    private static bool IsRegexMetacharacter(char c) =>
        c is '.' or '*' or '+' or '?' or '[' or ']' or '(' or ')' or '{' or '}' or
            '|' or '^' or '$';
}
