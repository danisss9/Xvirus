namespace YaraTranslator.Parsing;

public enum DiagnosticSeverity
{
    Info,
    Warning,
    Error
}

public sealed record SourceSpan(string File, int Line, int Column, int Length = 0)
{
    public override string ToString() => $"{File}:{Line}:{Column}";
}

public sealed record Diagnostic(
    string Code,
    DiagnosticSeverity Severity,
    string Message,
    SourceSpan? Span = null,
    string? Rule = null)
{
    public override string ToString()
    {
        var location = Span is null ? string.Empty : $"{Span} ";
        var rule = Rule is null ? string.Empty : $"[{Rule}] ";
        return $"{location}{rule}{Severity}: {Code}: {Message}";
    }
}

public sealed class DiagnosticBag
{
    private readonly List<Diagnostic> _items = new();

    public IReadOnlyList<Diagnostic> Items => _items;
    public bool HasErrors => _items.Any(d => d.Severity == DiagnosticSeverity.Error);

    public void Add(Diagnostic diagnostic) => _items.Add(diagnostic);

    public void Add(string code, DiagnosticSeverity severity, string message, SourceSpan? span = null, string? rule = null)
        => _items.Add(new Diagnostic(code, severity, message, span, rule));

    public void AddRange(IEnumerable<Diagnostic> diagnostics) => _items.AddRange(diagnostics);

    public void Error(string code, string message, SourceSpan? span = null, string? rule = null)
        => Add(code, DiagnosticSeverity.Error, message, span, rule);

    public void Warning(string code, string message, SourceSpan? span = null, string? rule = null)
        => Add(code, DiagnosticSeverity.Warning, message, span, rule);

    public void Info(string code, string message, SourceSpan? span = null, string? rule = null)
        => Add(code, DiagnosticSeverity.Info, message, span, rule);
}
