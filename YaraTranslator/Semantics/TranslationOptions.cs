namespace YaraTranslator.Semantics;

public enum TargetSelection
{
    Auto,
    Pe,
    Script,
    Both
}

public enum TargetKind
{
    Pe,
    Script
}

public enum EngineProfile
{
    Legacy,
    Strict
}

public enum CollisionPolicy
{
    Error,
    Skip,
    Keep,
    Overwrite
}

public enum RuleStatus
{
    Exact,
    Conditional,
    Partial,
    Skipped
}

public sealed class TranslationOptions
{
    public TargetSelection Target { get; set; } = TargetSelection.Auto;
    public EngineProfile Engine { get; set; } = EngineProfile.Strict;
    public bool AllowApproximations { get; set; }
    public string NameTemplate { get; set; } = "YARA.{rule}";
    public string Namespace { get; set; } = string.Empty;
    public int MinPatternBytes { get; set; } = 4;
    public int MaxPatternBytes { get; set; } = 4096;
    public int MaxVariantsPerString { get; set; } = 256;
    public int MaxTermsPerRule { get; set; } = 1024;
    public int MaxLinesPerRule { get; set; } = 4096;
    public int MaxTotalLines { get; set; } = 200_000;
    public int MaxInputBytes { get; set; } = 16 * 1024 * 1024;
    public int MaxIncludeDepth { get; set; } = 32;
    public List<string> IncludeDirectories { get; } = new();
    public Dictionary<string, object?> ExternalVariables { get; } = new(StringComparer.Ordinal);
    public CollisionPolicy CollisionPolicy { get; set; } = CollisionPolicy.Error;
    public string? MergeDatabase { get; set; }
    public string? OutputPath { get; set; }
    public string? ReportPath { get; set; }
    public string? ManifestPath { get; set; }
    public bool Prune { get; set; }
    public bool DryRun { get; set; }
    public bool Quiet { get; set; }

    public TranslationOptions Clone()
    {
        var clone = new TranslationOptions
        {
            Target = Target,
            Engine = Engine,
            AllowApproximations = AllowApproximations,
            NameTemplate = NameTemplate,
            Namespace = Namespace,
            MinPatternBytes = MinPatternBytes,
            MaxPatternBytes = MaxPatternBytes,
            MaxVariantsPerString = MaxVariantsPerString,
            MaxTermsPerRule = MaxTermsPerRule,
            MaxLinesPerRule = MaxLinesPerRule,
            MaxTotalLines = MaxTotalLines,
            MaxInputBytes = MaxInputBytes,
            MaxIncludeDepth = MaxIncludeDepth,
            CollisionPolicy = CollisionPolicy,
            MergeDatabase = MergeDatabase,
            OutputPath = OutputPath,
            ReportPath = ReportPath,
            ManifestPath = ManifestPath,
            Prune = Prune,
            DryRun = DryRun,
            Quiet = Quiet
        };

        clone.IncludeDirectories.AddRange(IncludeDirectories);
        foreach (var pair in ExternalVariables)
            clone.ExternalVariables[pair.Key] = pair.Value;

        return clone;
    }
}
