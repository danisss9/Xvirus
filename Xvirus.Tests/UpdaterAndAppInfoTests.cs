using Xunit;
using Xvirus.Model;

namespace Xvirus.Tests;

public class UpdaterAndAppInfoTests
{
    [Theory]
    [InlineData("4.5.0.0", "5.0.0.0", false)]
    [InlineData("5.0.0.0", "5.0.0.0", false)]
    [InlineData("5.0.0.1", "5.0.0.0", true)]
    [InlineData("5.1.0.0", "5.0.0.0", true)]
    [InlineData("6.0.0.0", "5.0.0.0", true)]
    [InlineData("", "5.0.0.0", false)]
    [InlineData(null, "5.0.0.0", false)]
    public void Updater_IsNewerVersion_UsesSemverComparison(string? remote, string local, bool expected)
    {
        Assert.Equal(expected, Updater.IsNewerVersion(remote, local));
    }

    [Fact]
    public void AppInfo_SideBySideMetadata_DiffersBetweenAmAndFw()
    {
        var prev = AppInfo.AppCode;
        try
        {
            AppInfo.AppCode = "antimalware";
            Assert.Equal(5236, AppInfo.ServicePort);
            Assert.Equal("XvirusAntiMalwareService", AppInfo.ServiceName);
            Assert.Equal("Xvirus Anti-Malware", AppInfo.ProductName);
            Assert.Equal("XvirusAM_", AppInfo.EtwSessionPrefix);

            AppInfo.AppCode = "firewall";
            Assert.Equal(5237, AppInfo.ServicePort);
            Assert.Equal("XvirusFirewallService", AppInfo.ServiceName);
            Assert.Equal("Xvirus Firewall", AppInfo.ProductName);
            Assert.Equal("XvirusFW_", AppInfo.EtwSessionPrefix);
        }
        finally
        {
            AppInfo.AppCode = prev;
        }
    }

    [Fact]
    public void Scanner_UpdateSettings_ReplacesActiveSettingsReference()
    {
        var s1 = new SettingsDTO { EnableSignatures = true, HeuristicsLevel = 3 };
        var s2 = new SettingsDTO { EnableSignatures = false, HeuristicsLevel = 5 };
        var db = new DB(s1);
        var ai = new AI(s1);
        var aiScript = new AIScript(s1);
        var rules = new Rules("rules_scanner_test.json", seedRunningProcesses: false);

        var scanner = new Scanner(s1, db, ai, aiScript, rules);
        Assert.Same(s1, scanner.settings);

        scanner.UpdateSettings(s2);
        Assert.Same(s2, scanner.settings);
        Assert.False(scanner.settings.EnableSignatures);
        Assert.Equal(5, scanner.settings.HeuristicsLevel);
    }
}
