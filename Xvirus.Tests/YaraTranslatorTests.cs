using System.Text;
using Xunit;
using Xvirus;
using Xvirus.Model;
using YaraTranslator.Emit;
using YaraTranslator.Parsing;
using YaraTranslator.Semantics;
using YaraTranslator.Validation;

namespace Xvirus.Tests;

public sealed class YaraTranslatorTests
{
    [Fact]
    public void CommandLineParsesTranslateOptions()
    {
        var parsed = YaraTranslator.Cli.CommandLine.Parse(new[]
        {
            "translate", "rules.yar", "--target=script", "--define", "FLAG=true",
            "--max-variants", "512", "--allow-approximations", "--collision", "skip"
        });

        Assert.Null(parsed.Error);
        Assert.NotNull(parsed.Options);
        var options = parsed.Options!;
        Assert.Equal(TargetSelection.Script, options.Translation.Target);
        Assert.True(options.Translation.AllowApproximations);
        Assert.Equal(512, options.Translation.MaxVariantsPerString);
        Assert.Equal(CollisionPolicy.Skip, options.Translation.CollisionPolicy);
        Assert.Equal(true, options.Translation.ExternalVariables["FLAG"]);
        Assert.Contains("rules.yar", options.Inputs);
    }

    [Fact]
    public void ParsesBasicRules()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "YaraTranslator", "samples", "basic.yar");
        var diagnostics = new DiagnosticBag();
        var file = YaraParser.Parse(File.ReadAllText(path), path, diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(2, file.Rules.Count);
        Assert.Equal("Example", file.Rules[0].Name);
        Assert.Contains("test", file.Rules[0].Tags);
        Assert.Equal(2, file.Rules[0].Strings.Count);
        Assert.NotNull(file.Rules[0].Condition);
    }

    [Fact]
    public void AllOfBecomesAnchorAndDependency()
    {
        var result = Translate("""
            rule AllOf
            {
                strings:
                    $a = "foobar"
                    $b = "bazqux"
                condition:
                    all of them
            }
            """);

        var rule = Assert.Single(result.Rules);
        Assert.Equal(RuleStatus.Exact, rule.Status);
        var line = Assert.Single(result.Lines);
        Assert.Single(line.Dependencies);
        Assert.NotEqual(line.AnchorHex, line.Dependencies[0].PatternHex);
        var expected = new[] { "666F6F626172", "62617A717578" };
        Assert.Contains(line.AnchorHex, expected);
        Assert.Contains(line.Dependencies[0].PatternHex, expected);
    }

    [Fact]
    public void SinglePatternUsesSelfDependency()
    {
        var result = Translate("""
            rule Single
            {
                strings:
                    $a = "foobar"
                condition:
                    $a
            }
            """);

        var line = Assert.Single(result.Lines);
        Assert.Single(line.Dependencies);
        Assert.Equal(line.AnchorHex, line.Dependencies[0].PatternHex);
        Assert.False(line.Dependencies[0].Negated);
    }

    [Fact]
    public void OrAndTermsGetUniqueAnchors()
    {
        var result = Translate("""
            rule OrAnd
            {
                strings:
                    $a = "iota1234"
                    $b = "kappa123"
                    $c = "lambda12"
                condition:
                    ($a and $b) or ($a and $c)
            }
            """);

        Assert.Equal(2, result.Lines.Count);
        Assert.Equal(2, result.Lines.Select(l => l.AnchorHex).Distinct().Count());
        Assert.All(result.Lines, l => Assert.True(l.Dependencies.Count > 0));
    }

    [Fact]
    public void NotBecomesNegativeDependency()
    {
        var result = Translate("""
            rule NotB
            {
                strings:
                    $a = "eta12345"
                    $b = "theta123"
                condition:
                    $a and not $b
            }
            """);

        var line = Assert.Single(result.Lines);
        Assert.Contains(line.Dependencies, d => d.Negated);
    }

    [Fact]
    public void NoCaseExpandsVariants()
    {
        var options = new TranslationOptions { Target = TargetSelection.Pe };
        var lowering = new StringLowering(options);
        var source = new YaraString
        {
            Id = "$a",
            Kind = YaraStringKind.Text,
            TextBytes = Encoding.ASCII.GetBytes("Abcd"),
            TextValue = "Abcd",
            NoCase = true
        };

        var lowered = lowering.Lower(source, "NoCase");

        Assert.Equal(16, lowered.Variants.Count);
        Assert.Contains(lowered.Variants, v => v.Hex == "41424344");
        Assert.Contains(lowered.Variants, v => v.Hex == "61626364");
    }

    [Fact]
    public void HexWildcardExpandsExactly()
    {
        var result = Translate("""
            rule HexWildcard
            {
                strings:
                    $a = { 66 6F 6F 62 61 72 ?? }
                condition:
                    $a
            }
            """);

        Assert.Equal(256, result.Lines.Count);
        Assert.All(result.Lines, l => Assert.Equal(7, l.AnchorHex.Length / 2));
    }

    [Fact]
    public void RegexIsSkippedByDefault()
    {
        var result = Translate("""
            rule RegexRule
            {
                strings:
                    $a = /foobar[0-9]+bazqux/
                condition:
                    $a
            }
            """);

        var rule = Assert.Single(result.Rules);
        Assert.Equal(RuleStatus.Skipped, rule.Status);
        Assert.Empty(result.Lines);
        Assert.Equal(0, result.Summary.Errors);
        Assert.True(result.Summary.Warnings > 0);
    }

    [Fact]
    public void RegexCanBeApproximated()
    {
        var result = Translate("""
            rule RegexRule
            {
                strings:
                    $a = /foobar[0-9]+bazqux/
                condition:
                    $a
            }
            """, options => options.AllowApproximations = true);

        var rule = Assert.Single(result.Rules);
        Assert.Equal(RuleStatus.Partial, rule.Status);
        Assert.Single(result.Lines);
    }

    [Fact]
    public void CountIsSkippedByDefaultAndApproximatedOnRequest()
    {
        const string ruleText = """
            rule Count
            {
                strings:
                    $a = "foobar"
                condition:
                    #a > 2
            }
            """;

        var strict = Translate(ruleText);
        Assert.Equal(RuleStatus.Skipped, Assert.Single(strict.Rules).Status);

        var approximate = Translate(ruleText, options => options.AllowApproximations = true);
        Assert.Equal(RuleStatus.Partial, Assert.Single(approximate.Rules).Status);
        Assert.Single(approximate.Lines);
    }

    [Fact]
    public void FileSizeIsSkippedByDefaultAndApproximatedOnRequest()
    {
        const string ruleText = """
            rule FileSize
            {
                strings:
                    $a = "foobar"
                condition:
                    filesize < 1MB and $a
            }
            """;

        Assert.Equal(RuleStatus.Skipped, Assert.Single(Translate(ruleText).Rules).Status);

        var approximate = Translate(ruleText, options => options.AllowApproximations = true);
        Assert.Equal(RuleStatus.Partial, Assert.Single(approximate.Rules).Status);
        Assert.Single(approximate.Lines);
    }

    [Fact]
    public void GlobalAndPrivateRulesAreInlined()
    {
        var result = Translate("""
            global rule Always
            {
                strings:
                    $g = "globalmarker"
                condition:
                    $g
            }

            rule UsesGlobal
            {
                strings:
                    $a = "foobar"
                condition:
                    $a
            }

            private rule PrivateHelper
            {
                strings:
                    $p = "privatemarker"
                condition:
                    $p
            }

            rule UsesPrivate
            {
                strings:
                    $a = "bazqux"
                condition:
                    $a and PrivateHelper
            }
            """);

        Assert.Equal(2, result.Lines.Count);

        var usesGlobal = result.Lines.Single(l => l.Name == "YARA.UsesGlobal");
        Assert.Equal("676C6F62616C6D61726B6572", usesGlobal.AnchorHex);
        Assert.Contains(usesGlobal.Dependencies, d => d.PatternHex == "666F6F626172");

        var usesPrivate = result.Lines.Single(l => l.Name == "YARA.UsesPrivate");
        Assert.Equal("707269766174656D61726B6572", usesPrivate.AnchorHex);
        Assert.Contains(usesPrivate.Dependencies, d => d.PatternHex == "62617A717578");
        Assert.Contains(usesPrivate.Dependencies, d => d.PatternHex == "676C6F62616C6D61726B6572");

        Assert.Equal(RuleStatus.Skipped, result.Rules.Single(r => r.Rule.Name == "Always").Status);
        Assert.Equal(RuleStatus.Skipped, result.Rules.Single(r => r.Rule.Name == "PrivateHelper").Status);
    }

    [Fact]
    public void TargetBothOmitsPeOnlyRuleFromScript()
    {
        var result = Translate("""
            rule PeOnly
            {
                strings:
                    $a = "foobar"
                condition:
                    pe.is_pe and $a
            }
            """, options => options.Target = TargetSelection.Both);

        var rule = Assert.Single(result.Rules);
        Assert.Equal(RuleStatus.Exact, rule.Status);
        Assert.Single(result.Lines);
        Assert.Equal(TargetKind.Pe, result.Lines[0].Target);
        Assert.Empty(result.LinesFor(TargetKind.Script));
    }

    [Fact]
    public void MetaTargetSelectsScriptDatabase()
    {
        var result = Translate("""
            rule ScriptRule
            {
                meta:
                    target = "script"
                strings:
                    $a = "foobar"
                condition:
                    $a
            }
            """, options => options.Target = TargetSelection.Auto);

        var line = Assert.Single(result.Lines);
        Assert.Equal(TargetKind.Script, line.Target);
    }

    [Fact]
    public void NoneOfBecomesNegativeDependency()
    {
        var result = Translate("""
            rule NoneOf
            {
                strings:
                    $a = "foobar"
                    $b = "bazqux"
                condition:
                    $a and none of ($b*)
            }
            """);

        var line = Assert.Single(result.Lines);
        Assert.Contains(line.Dependencies, d => d.Negated && d.PatternHex == "62617A717578");
    }

    [Fact]
    public void WriterAcceptsDependencyOrderChangesAsEquivalent()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "heurlist.db");
        var first = Line("666F6F626172", "YARA.One", "62617A717578", "71757578");
        new HeuristicDbWriter().Write(new[] { first }, path, null, CollisionPolicy.Error);

        var reordered = Line("666F6F626172", "YARA.One", "71757578", "62617A717578");
        var result = new HeuristicDbWriter().Write(new[] { reordered }, path, path, CollisionPolicy.Error);

        Assert.Equal(0, result.Added);
        Assert.Equal(1, result.Skipped);
    }

    [Fact]
    public void EmptyStringIsSkippedWithoutCrashing()
    {
        var result = Translate("""
            rule Empty
            {
                strings:
                    $a = "" nocase
                condition:
                    $a
            }
            """);

        var rule = Assert.Single(result.Rules);
        Assert.Equal(RuleStatus.Skipped, rule.Status);
        Assert.Empty(result.Lines);
    }

    [Fact]
    public void IncludeResolverLoadsIncludedRules()
    {
        using var temp = new TempDirectory();
        var main = Path.Combine(temp.Path, "main.yar");
        var common = Path.Combine(temp.Path, "common.yar");

        File.WriteAllText(common, """
            rule Common
            {
                strings:
                    $a = "commonmarker"
                condition:
                    $a
            }
            """);
        File.WriteAllText(main, """
            include "common.yar"
            rule Main
            {
                strings:
                    $a = "mainmarker"
                condition:
                    $a
            }
            """);

        var result = new TranslationEngine(new TranslationOptions
        {
            Target = TargetSelection.Pe,
            Engine = EngineProfile.Strict
        }).Translate(new[] { main });

        Assert.Contains(result.Rules, r => r.Rule.Name == "Common");
        Assert.Contains(result.Rules, r => r.Rule.Name == "Main");
        Assert.False(result.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error));
    }

    [Fact]
    public void IncludeOutsideAllowedRootIsRejected()
    {
        using var temp = new TempDirectory();
        var root = Path.Combine(temp.Path, "root");
        Directory.CreateDirectory(root);
        var main = Path.Combine(root, "main.yar");
        var outside = Path.Combine(temp.Path, "outside.yar");

        File.WriteAllText(outside, """
            rule Outside
            {
                strings:
                    $a = "outsidemarker"
                condition:
                    $a
            }
            """);
        File.WriteAllText(main, """
            include "../outside.yar"
            rule Main
            {
                strings:
                    $a = "mainmarker"
                condition:
                    $a
            }
            """);

        var result = new TranslationEngine(new TranslationOptions
        {
            Target = TargetSelection.Pe,
            Engine = EngineProfile.Strict
        }).Translate(new[] { main });

        Assert.Contains(result.Diagnostics, d =>
            d.Severity == DiagnosticSeverity.Error && d.Code == "YARA1205");
        Assert.DoesNotContain(result.Rules, r => r.Rule.Name == "Outside");
        Assert.Contains(result.Rules, r => r.Rule.Name == "Main");
    }

    [Fact]
    public void ParserDoesNotThrowOnMalformedInput()
    {
        const string alphabet = "abcxyz0129$#@!{}()[]:=,.\"\\/+-*%&|^~ \r\n\t";
        var random = new Random(12345);

        for (var i = 0; i < 200; i++)
        {
            var length = random.Next(0, 250);
            var chars = new char[length];
            for (var j = 0; j < length; j++)
                chars[j] = alphabet[random.Next(alphabet.Length)];

            var diagnostics = new DiagnosticBag();
            var exception = Record.Exception(() => YaraParser.Parse(new string(chars), $"fuzz{i}.yar", diagnostics));
            Assert.Null(exception);
        }
    }

    [Fact]
    public void WriterAndValidatorRoundTrip()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "heurlist.db");
        var lines = new[]
        {
            Line("666F6F626172", "YARA.One", "62617A717578")
        };

        var writer = new HeuristicDbWriter();
        var result = writer.Write(lines, path, null, CollisionPolicy.Error);

        Assert.True(result.WroteFile);
        Assert.Equal(1, result.Added);
        Assert.True(File.Exists(path));

        var validation = HeuristicDbValidator.Validate(path);
        Assert.False(validation.HasErrors);

        var bytes = File.ReadAllBytes(path);
        Assert.Contains((byte)'\r', bytes);
        Assert.Contains((byte)'\n', bytes);
    }

    [Fact]
    public void WriterMergeIsIdempotent()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "heurlist.db");
        var lines = new[] { Line("666F6F626172", "YARA.One", "62617A717578") };

        var first = new HeuristicDbWriter().Write(lines, path, null, CollisionPolicy.Error);
        var second = new HeuristicDbWriter().Write(lines, path, path, CollisionPolicy.Error);

        Assert.Equal(1, first.Added);
        Assert.Equal(0, second.Added);
        Assert.Equal(1, second.Skipped);
        Assert.Equal(1, second.Lines);
    }

    [Fact]
    public void WriterPrunesPreviouslyOwnedLines()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "heurlist.db");
        var first = new[]
        {
            Line("666F6F626172", "YARA.One", "62617A717578"),
            Line("71757578", "YARA.Two", "666F6F626172")
        };

        new HeuristicDbWriter().Write(first, path, null, CollisionPolicy.Error);

        var keep = Line("666F6F626172", "YARA.One", "62617A717578");
        var previousKeys = first.Select(l => new DbEntry
        {
            AnchorHex = l.AnchorHex,
            Name = l.Name,
            Dependencies = l.Dependencies,
            OriginalLine = l.ToDbLine()
        }.CanonicalKey).ToHashSet(StringComparer.Ordinal);

        var result = new HeuristicDbWriter().Write(
            new[] { keep },
            path,
            path,
            CollisionPolicy.Error,
            previousKeys);

        Assert.Equal(1, result.Pruned);
        Assert.Equal(1, result.Lines);
        var text = File.ReadAllText(path);
        Assert.Contains("YARA.One", text);
        Assert.DoesNotContain("YARA.Two", text);
    }

    [Fact]
    public void WriterPreservesMalformedExistingLinesDuringMerge()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "heurlist.db");
        File.WriteAllText(path, "6-1-2,Existing.One\r\n666F6F626172,Existing.Two\r\n");

        var result = new HeuristicDbWriter().Write(
            new[] { Line("71757578", "YARA.New", "62617A717578") },
            path,
            path,
            CollisionPolicy.Error);

        Assert.Equal(1, result.Added);
        Assert.Equal(3, result.Lines);
        var text = File.ReadAllText(path);
        Assert.Contains("6-1-2,Existing.One", text);
        Assert.Contains("666F6F626172,Existing.Two", text);
        Assert.Contains("YARA.New", text);
    }

    [Fact]
    public void WriterRejectsAnchorCollisionByDefault()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "heurlist.db");
        new HeuristicDbWriter().Write(
            new[] { Line("666F6F626172", "YARA.One", "62617A717578") },
            path,
            null,
            CollisionPolicy.Error);

        Assert.Throws<DbMergeException>(() => new HeuristicDbWriter().Write(
            new[] { Line("666F6F626172", "YARA.Two", "71757578") },
            path,
            path,
            CollisionPolicy.Error));
    }

    [Fact]
    public void GeneratedDatabaseLoadsInBaseLibrary()
    {
        using var temp = new TempDirectory();
        var dbDir = Path.Combine(temp.Path, "Database");
        Directory.CreateDirectory(dbDir);

        var result = Translate("""
            rule AllOf
            {
                strings:
                    $a = "foobar"
                    $b = "bazqux"
                condition:
                    all of them
            }
            """, options => options.Target = TargetSelection.Script);

        var line = Assert.Single(result.Lines);
        new HeuristicDbWriter().Write(result.Lines, Path.Combine(dbDir, "heurlist2.db"), null, CollisionPolicy.Error);

        var settings = new SettingsDTO
        {
            DatabaseFolder = dbDir,
            EnableSignatures = false,
            EnableHeuristics = true,
            EnableAIScan = false,
            EnableAIScriptScan = false
        };

        var db = new DB(settings);
        Assert.NotNull(db.heurScriptListPatterns);
        Assert.True(db.heurScriptList.ContainsKey(line.AnchorHex));
        Assert.True(db.heurScriptListDeps.ContainsKey(line.AnchorHex));

        var dep = Assert.Single(line.Dependencies);
        Assert.Contains(dep.PatternHex, db.heurScriptListDeps[line.AnchorHex]);

        var sample = Path.Combine(temp.Path, "sample.bin");
        File.WriteAllBytes(sample, Encoding.ASCII.GetBytes("xxfoobarxxbazquxxx"));
        using var stream = File.OpenRead(sample);
        var matches = db.heurScriptListPatterns!.Search(stream).ToHashSet();
        Assert.Contains(line.AnchorHex, matches);
        Assert.Contains(dep.PatternHex, matches);
    }

    [Fact]
    public void ScannerDetectsSinglePatternAtDefaultLevel()
    {
        using var temp = new TempDirectory();
        var dbDir = Path.Combine(temp.Path, "Database");
        Directory.CreateDirectory(dbDir);

        var result = Translate("""
            rule Single
            {
                strings:
                    $a = "foobar"
                condition:
                    $a
            }
            """, options => options.Target = TargetSelection.Script);

        Assert.Single(result.Lines);
        new HeuristicDbWriter().Write(result.Lines, Path.Combine(dbDir, "heurlist2.db"), null, CollisionPolicy.Error);

        var settings = new SettingsDTO
        {
            DatabaseFolder = dbDir,
            EnableSignatures = false,
            EnableHeuristics = true,
            EnableAIScan = false,
            EnableAIScriptScan = false,
            HeuristicsLevel = 4
        };

        var sample = Path.Combine(temp.Path, "sample.txt");
        File.WriteAllBytes(sample, Encoding.ASCII.GetBytes("xxfoobarxx"));

        var db = new DB(settings);
        using var ai = new AI(settings);
        var aiScript = new AIScript(settings);
        var rules = new Rules(Path.Combine(temp.Path, "rules.json"), seedRunningProcesses: false);
        using var scanner = new Scanner(settings, db, ai, aiScript, rules);

        var scan = scanner.ScanFile(sample);
        Assert.True(scan.IsMalware);
        Assert.Equal("YARA.Single", scan.Name);
    }

    [Fact]
    public void ScannerHonoursAndAndNotDependenciesAtDefaultLevel()
    {
        using var temp = new TempDirectory();
        var dbDir = Path.Combine(temp.Path, "Database");
        Directory.CreateDirectory(dbDir);

        var result = Translate("""
            rule AndNot
            {
                strings:
                    $a = "foobar"
                    $b = "bazqux"
                condition:
                    $a and not $b
            }
            """, options => options.Target = TargetSelection.Script);

        new HeuristicDbWriter().Write(result.Lines, Path.Combine(dbDir, "heurlist2.db"), null, CollisionPolicy.Error);

        var settings = new SettingsDTO
        {
            DatabaseFolder = dbDir,
            EnableSignatures = false,
            EnableHeuristics = true,
            EnableAIScan = false,
            EnableAIScriptScan = false,
            HeuristicsLevel = 4
        };

        var db = new DB(settings);
        using var ai = new AI(settings);
        var aiScript = new AIScript(settings);
        var rules = new Rules(Path.Combine(temp.Path, "rules.json"), seedRunningProcesses: false);
        using var scanner = new Scanner(settings, db, ai, aiScript, rules);

        var positive = Path.Combine(temp.Path, "positive.txt");
        File.WriteAllBytes(positive, Encoding.ASCII.GetBytes("xxfoobarxx"));
        Assert.True(scanner.ScanFile(positive).IsMalware);

        var negative = Path.Combine(temp.Path, "negative.txt");
        File.WriteAllBytes(negative, Encoding.ASCII.GetBytes("xxfoobarxxbazquxxx"));
        Assert.False(scanner.ScanFile(negative).IsMalware);
    }

    private static TranslationResult Translate(string ruleText, Action<TranslationOptions>? configure = null)
    {
        var temp = new TempDirectory();
        try
        {
            var path = Path.Combine(temp.Path, "rule.yar");
            File.WriteAllText(path, ruleText, new UTF8Encoding(false));

            var options = new TranslationOptions
            {
                Target = TargetSelection.Pe,
                Engine = EngineProfile.Strict
            };
            configure?.Invoke(options);

            return new TranslationEngine(options).Translate(new[] { path });
        }
        finally
        {
            temp.Dispose();
        }
    }

    private static SignatureLine Line(string anchor, string name, params string[] deps)
    {
        return new SignatureLine
        {
            AnchorHex = anchor,
            Name = name,
            Dependencies = deps.Select(d => d.StartsWith('!')
                ? new Dependency(d[1..], true)
                : new Dependency(d, false)).ToList(),
            Target = TargetKind.Pe,
            RuleName = name
        };
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; }

        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "YaraTranslatorTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                    Directory.Delete(Path, recursive: true);
            }
            catch
            {
                // Best-effort cleanup for locked native files on Windows.
            }
        }
    }
}
