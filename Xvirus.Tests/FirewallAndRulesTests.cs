using System.Text.Json;
using BaseLibrary.Serializers;
using Xunit;
using Xvirus.Model;

namespace Xvirus.Tests;

public class FirewallAndRulesTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _prevDir;
    private readonly string _prevAppCode;

    public FirewallAndRulesTests()
    {
        _prevDir = Utils.CurrentDir;
        _prevAppCode = AppInfo.AppCode;
        _tempDir = Path.Combine(Path.GetTempPath(), "xvirus-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        Utils.CurrentDir = _tempDir;
        // Keep AppCode as "sdk" during unit tests so Firewall.BlockRule never touches host Windows Firewall.
        AppInfo.AppCode = "sdk";
    }

    public void Dispose()
    {
        Utils.CurrentDir = _prevDir;
        AppInfo.AppCode = _prevAppCode;
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void Firewall_RuleNameFor_PreservesLegacyProgramNaming_AndDisambiguatesKinds()
    {
        var path = @"C:\Program Files\Test\app.exe";
        var legacy = Firewall.RuleNameFor(path);
        var programRule = new Rule { Kind = RuleKind.Program, Path = path, Protocol = "any", Direction = "both" };
        var ipRule = new Rule { Kind = RuleKind.Ip, Path = "1.2.3.4" };
        var portRule = new Rule { Kind = RuleKind.Port, Path = "445", Protocol = "tcp", Direction = "in" };

        Assert.Equal(legacy, Firewall.RuleNameFor(programRule));
        Assert.StartsWith(Firewall.RulePrefix, Firewall.RuleNameFor(ipRule));
        Assert.StartsWith(Firewall.RulePrefix, Firewall.RuleNameFor(portRule));
        Assert.NotEqual(Firewall.RuleNameFor(ipRule), Firewall.RuleNameFor(portRule));
    }

    [Fact]
    public void Firewall_BuildAddRuleArgs_BuildsExpectedNetshArguments()
    {
        var ipArgs = Firewall.BuildAddRuleArgs("Xvirus_Block_1234567890ABCDEF", RuleKind.Ip, "203.0.113.5", "out", "tcp", "public");
        Assert.Contains("name=Xvirus_Block_1234567890ABCDEF", ipArgs);
        Assert.Contains("dir=out", ipArgs);
        Assert.Contains("action=block", ipArgs);
        Assert.Contains("remoteip=203.0.113.5", ipArgs);
        Assert.Contains("protocol=tcp", ipArgs);
        Assert.Contains("profile=public", ipArgs);

        var portInArgs = Firewall.BuildAddRuleArgs("Xvirus_Block_1234567890ABCDEF", RuleKind.Port, "445", "in", "tcp", "private");
        Assert.Contains("localport=445", portInArgs);
        Assert.Contains("profile=private", portInArgs);

        var portOutArgs = Firewall.BuildAddRuleArgs("Xvirus_Block_1234567890ABCDEF", RuleKind.Port, "8000-8080", "out", "udp", "domain");
        Assert.Contains("remoteport=8000-8080", portOutArgs);
        Assert.Contains("protocol=udp", portOutArgs);
        Assert.Contains("profile=domain", portOutArgs);
    }

    [Theory]
    [InlineData("1.2.3.4", true)]
    [InlineData("10.0.0.0/8", true)]
    [InlineData("2001:db8::/32", true)]
    [InlineData("10.0.0.0/33", false)]
    [InlineData("not-an-ip", false)]
    [InlineData("", false)]
    public void Firewall_IsValidIpOrCidr_ValidatesCorrectly(string input, bool expected)
    {
        Assert.Equal(expected, Firewall.IsValidIpOrCidr(input));
    }

    [Theory]
    [InlineData("80", true)]
    [InlineData("443", true)]
    [InlineData("8000-8080", true)]
    [InlineData("0", false)]
    [InlineData("70000", false)]
    [InlineData("9000-8000", false)]
    [InlineData("abc", false)]
    public void Firewall_IsValidPortSpec_ValidatesCorrectly(string input, bool expected)
    {
        Assert.Equal(expected, Firewall.IsValidPortSpec(input));
    }

    [Fact]
    public void Rules_DeserializesLegacyJson_WithoutKindOrProfile()
    {
        var legacyJson = """{"Id":"legacy-1","Path":"C:\\Tools\\old.exe","Type":"block"}""";
        var rule = JsonSerializer.Deserialize(legacyJson, SourceGenerationContext.Default.Rule);

        Assert.NotNull(rule);
        Assert.Equal("legacy-1", rule!.Id);
        Assert.Equal(RuleKind.Program, rule.Kind);
        Assert.Equal(RuleType.Block, rule.Type);
        Assert.Equal("any", rule.Protocol);
        Assert.Equal("both", rule.Direction);
        Assert.Equal("any", rule.Profile);
    }

    [Fact]
    public void Rules_FlippingAllowToBlock_ReusesIdAndDoesNotDuplicate()
    {
        var rules = new Rules("rules_test.json", seedRunningProcesses: false);
        var r1 = rules.AddAllowRule(@"C:\Test\demo.exe", "rules_test.json");
        var r2 = rules.AddBlockRule(@"C:\Test\demo.exe", "rules_test.json");

        Assert.Equal(r1.Id, r2.Id);
        Assert.Equal(RuleType.Block, rules.GetRuleType(@"C:\Test\demo.exe"));
        Assert.Single(rules.GetAllRules());
    }

    [Fact]
    public void Rules_DomainResolution_MatchesExactAndSubdomain()
    {
        var rules = new Rules("rules_domain.json", seedRunningProcesses: false);
        rules.AddRule(new Rule { Kind = RuleKind.Domain, Path = "bad.example.com", Type = RuleType.Block }, "rules_domain.json");

        Assert.True(rules.EnforceDomainResolution("bad.example.com", "203.0.113.10"));
        Assert.True(rules.EnforceDomainResolution("sub.bad.example.com", "203.0.113.11"));
        Assert.False(rules.EnforceDomainResolution("good.example.com", "203.0.113.12"));
    }

    [Fact]
    public async Task Rules_ConcurrentReadsAndWrites_AreThreadSafe()
    {
        var rules = new Rules("rules_concurrent.json", seedRunningProcesses: false);
        var tasks = new List<Task>();

        for (int i = 0; i < 4; i++)
        {
            int worker = i;
            tasks.Add(Task.Run(() =>
            {
                for (int j = 0; j < 25; j++)
                {
                    var p = $@"C:\Concurrent\app_{worker}_{j}.exe";
                    var r = rules.AddBlockRule(p, "rules_concurrent.json");
                    _ = rules.GetRuleType(p);
                    _ = rules.GetAllRules();
                    if (j % 2 == 0) rules.RemoveRule(r.Id, "rules_concurrent.json");
                }
            }));
        }

        await Task.WhenAll(tasks);
        Assert.NotEmpty(rules.GetAllRules());
    }
}
