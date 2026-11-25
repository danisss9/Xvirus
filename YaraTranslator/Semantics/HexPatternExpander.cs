using System.Text;

namespace YaraTranslator.Semantics;

public sealed class ExpansionLimitException : Exception
{
    public ExpansionLimitException(string message) : base(message)
    {
    }
}

public sealed class HexExpansionResult
{
    public List<byte[]> Variants { get; } = new();
    public bool HasWildcard { get; set; }
    public bool HasJump { get; set; }
    public bool HasAlternative { get; set; }
    public bool HasNegation { get; set; }
}

/// <summary>
/// Parses and fully expands YARA hex strings when the expansion fits the configured
/// limit. A jump is arbitrary bytes, so it is only representable by enumerating every
/// possible byte sequence; larger jumps are rejected rather than approximated silently.
/// </summary>
public sealed class HexPatternExpander
{
    private readonly int _maxVariants;
    private readonly int _maxPatternBytes;

    public HexPatternExpander(int maxVariants, int maxPatternBytes)
    {
        _maxVariants = maxVariants;
        _maxPatternBytes = maxPatternBytes;
    }

    public HexExpansionResult Expand(string body)
    {
        var parser = new HexBodyParser(body);
        var nodes = parser.ParseSequence();
        parser.EnsureEnd();

        var result = new HexExpansionResult
        {
            HasWildcard = parser.HasWildcard,
            HasJump = parser.HasJump,
            HasAlternative = parser.HasAlternative,
            HasNegation = parser.HasNegation
        };

        var expanded = ExpandSequence(nodes);
        foreach (var bytes in expanded)
        {
            if (bytes.Length > _maxPatternBytes)
                throw new ExpansionLimitException($"Expanded hex pattern is longer than {_maxPatternBytes} bytes.");

            if (bytes.Length > 0)
                result.Variants.Add(bytes);
        }

        return result;
    }

    private List<byte[]> ExpandSequence(IReadOnlyList<HexNode> nodes)
    {
        var current = new List<byte[]> { Array.Empty<byte>() };

        foreach (var node in nodes)
        {
            var options = ExpandNode(node);
            if (options.Count == 0)
                return new List<byte[]>();

            if ((long)current.Count * options.Count > _maxVariants)
                throw new ExpansionLimitException($"Hex expansion exceeds {_maxVariants} variants.");

            var next = new List<byte[]>(current.Count * options.Count);
            foreach (var prefix in current)
            {
                foreach (var suffix in options)
                {
                    if (prefix.Length + suffix.Length > _maxPatternBytes)
                        throw new ExpansionLimitException($"Expanded hex pattern is longer than {_maxPatternBytes} bytes.");

                    var combined = new byte[prefix.Length + suffix.Length];
                    Buffer.BlockCopy(prefix, 0, combined, 0, prefix.Length);
                    Buffer.BlockCopy(suffix, 0, combined, prefix.Length, suffix.Length);
                    next.Add(combined);

                    if (next.Count > _maxVariants)
                        throw new ExpansionLimitException($"Hex expansion exceeds {_maxVariants} variants.");
                }
            }

            current = next;
        }

        return current;
    }

    private List<byte[]> ExpandNode(HexNode node)
    {
        switch (node)
        {
            case LiteralNode literal:
                return new List<byte[]> { new[] { literal.Value } };

            case ByteSetNode set:
                return set.Values.Select(v => new[] { v }).ToList();

            case JumpNode jump:
            {
                var list = new List<byte[]>();
                for (var length = jump.Min; length <= jump.Max; length++)
                {
                    var sequences = AllByteSequences(length);
                    if ((long)list.Count + sequences.Count > _maxVariants)
                        throw new ExpansionLimitException($"Hex jump expansion exceeds {_maxVariants} variants.");
                    list.AddRange(sequences);
                }

                return list;
            }

            case AlternativesNode alternatives:
            {
                var list = new List<byte[]>();
                foreach (var branch in alternatives.Branches)
                {
                    var expanded = ExpandSequence(branch);
                    if ((long)list.Count + expanded.Count > _maxVariants)
                        throw new ExpansionLimitException($"Hex alternative expansion exceeds {_maxVariants} variants.");
                    list.AddRange(expanded);
                }

                return list;
            }

            default:
                throw new InvalidOperationException("Unknown hex node.");
        }
    }

    private List<byte[]> AllByteSequences(int length)
    {
        if (length == 0)
            return new List<byte[]> { Array.Empty<byte>() };

        // 256^length is checked by the caller via the variant cap, but guard here too.
        if (length > 3)
            throw new ExpansionLimitException($"Hex jump of {length} bytes is too large to expand.");

        var count = 1;
        for (var i = 0; i < length; i++)
        {
            count *= 256;
            if (count > _maxVariants)
                throw new ExpansionLimitException($"Hex jump of {length} bytes exceeds {_maxVariants} variants.");
        }

        var result = new List<byte[]>(count);
        var current = new byte[length];
        for (var i = 0; i < count; i++)
        {
            var value = i;
            for (var b = length - 1; b >= 0; b--)
            {
                current[b] = (byte)(value & 0xFF);
                value >>= 8;
            }

            result.Add((byte[])current.Clone());
        }

        return result;
    }

    private abstract record HexNode;

    private sealed record LiteralNode(byte Value) : HexNode;

    private sealed record ByteSetNode(byte[] Values) : HexNode;

    private sealed record JumpNode(int Min, int Max) : HexNode;

    private sealed record AlternativesNode(IReadOnlyList<IReadOnlyList<HexNode>> Branches) : HexNode;

    private sealed class HexBodyParser
    {
        private readonly string _text;
        private int _position;

        public bool HasWildcard { get; private set; }
        public bool HasJump { get; private set; }
        public bool HasAlternative { get; private set; }
        public bool HasNegation { get; private set; }

        public HexBodyParser(string text)
        {
            _text = text;
        }

        private char Current => _position < _text.Length ? _text[_position] : '\0';

        private char Peek(int offset)
        {
            var index = _position + offset;
            return index < _text.Length ? _text[index] : '\0';
        }

        private bool AtEnd => _position >= _text.Length;

        private void SkipWhitespace()
        {
            while (!AtEnd && char.IsWhiteSpace(Current))
                _position++;
        }

        public IReadOnlyList<HexNode> ParseSequence()
        {
            var nodes = new List<HexNode>();
            SkipWhitespace();

            while (!AtEnd && Current != ')' && Current != '|')
            {
                var node = ParseItem();
                if (node is not null)
                    nodes.Add(node);
                SkipWhitespace();
            }

            return nodes;
        }

        public void EnsureEnd()
        {
            SkipWhitespace();
            if (!AtEnd)
                throw new ExpansionLimitException($"Unexpected character '{Current}' in hex string.");
        }

        private HexNode? ParseItem()
        {
            SkipWhitespace();
            if (AtEnd)
                return null;

            if (Current == '(')
            {
                HasAlternative = true;
                _position++;
                var branches = new List<IReadOnlyList<HexNode>>();
                while (true)
                {
                    branches.Add(ParseSequence());
                    SkipWhitespace();
                    if (Current == '|')
                    {
                        _position++;
                        continue;
                    }

                    if (Current == ')')
                    {
                        _position++;
                        break;
                    }

                    throw new ExpansionLimitException("Unterminated hex alternative.");
                }

                return new AlternativesNode(branches);
            }

            if (Current == '[')
            {
                HasJump = true;
                _position++;
                var min = ParseNumber();
                var max = min;
                SkipWhitespace();
                if (Current == '-')
                {
                    _position++;
                    max = ParseNumber();
                }

                SkipWhitespace();
                if (Current != ']')
                    throw new ExpansionLimitException("Unterminated hex jump.");
                _position++;

                if (max < min)
                    throw new ExpansionLimitException("Hex jump maximum is smaller than minimum.");
                return new JumpNode(min, max);
            }

            if (Current == '~')
            {
                HasNegation = true;
                _position++;
                var value = ParseByte();
                var values = Enumerable.Range(0, 256)
                    .Where(v => v != value)
                    .Select(v => (byte)v)
                    .ToArray();
                return new ByteSetNode(values);
            }

            if (Current == '?' || (IsHexDigit(Current) && Peek(1) == '?'))
            {
                HasWildcard = true;
                var values = ParseWildcard();
                return new ByteSetNode(values);
            }

            if (IsHexDigit(Current) && IsHexDigit(Peek(1)))
            {
                var value = ParseByte();
                return new LiteralNode(value);
            }

            throw new ExpansionLimitException($"Unexpected character '{Current}' in hex string.");
        }

        private int ParseNumber()
        {
            SkipWhitespace();
            var start = _position;
            while (!AtEnd && char.IsDigit(Current))
                _position++;

            if (start == _position)
                throw new ExpansionLimitException("Expected a number in hex jump.");

            return int.Parse(_text[start.._position], System.Globalization.CultureInfo.InvariantCulture);
        }

        private byte[] ParseWildcard()
        {
            if (Current == '?')
            {
                _position++;
                if (Current == '?')
                {
                    _position++;
                    return Enumerable.Range(0, 256).Select(v => (byte)v).ToArray();
                }

                var low = ParseNibble();
                return Enumerable.Range(0, 256)
                    .Where(v => (v & 0x0F) == low)
                    .Select(v => (byte)v)
                    .ToArray();
            }

            var high = ParseNibble();
            if (Current != '?')
                throw new ExpansionLimitException("Expected '?' in hex wildcard.");
            _position++;
            return Enumerable.Range(0, 256)
                .Where(v => (v >> 4) == high)
                .Select(v => (byte)v)
                .ToArray();
        }

        private byte ParseByte()
        {
            var high = ParseNibble();
            var low = ParseNibble();
            return (byte)((high << 4) | low);
        }

        private int ParseNibble()
        {
            var c = Current;
            var value = c switch
            {
                >= '0' and <= '9' => c - '0',
                >= 'a' and <= 'f' => c - 'a' + 10,
                >= 'A' and <= 'F' => c - 'A' + 10,
                _ => -1
            };

            if (value < 0)
                throw new ExpansionLimitException($"Expected a hex digit, found '{c}'.");

            _position++;
            return value;
        }

        private static bool IsHexDigit(char c) =>
            c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
    }
}
