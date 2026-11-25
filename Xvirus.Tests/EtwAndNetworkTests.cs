using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Xunit;
using XvirusService.Services;

namespace Xvirus.Tests;

[SupportedOSPlatform("windows")]
public class EtwAndNetworkTests
{
    [Fact]
    public void EtwNative_StructSizesAndOffsets_MatchWindowsX64Abi()
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8) return;

        Assert.Equal(48, Marshal.SizeOf<EtwNative.WNODE_HEADER>());
        Assert.Equal(120, Marshal.SizeOf<EtwNative.EVENT_TRACE_PROPERTIES>());
        Assert.Equal(448, Marshal.SizeOf<EtwNative.EVENT_TRACE_LOGFILEW>());

        Assert.Equal(112, Marshal.OffsetOf<EtwNative.EVENT_TRACE_PROPERTIES>(nameof(EtwNative.EVENT_TRACE_PROPERTIES.LogFileNameOffset)).ToInt32());
        Assert.Equal(116, Marshal.OffsetOf<EtwNative.EVENT_TRACE_PROPERTIES>(nameof(EtwNative.EVENT_TRACE_PROPERTIES.LoggerNameOffset)).ToInt32());
        Assert.Equal(8, Marshal.OffsetOf<EtwNative.EVENT_TRACE_LOGFILEW>(nameof(EtwNative.EVENT_TRACE_LOGFILEW.LoggerName)).ToInt32());
        Assert.Equal(28, Marshal.OffsetOf<EtwNative.EVENT_TRACE_LOGFILEW>(nameof(EtwNative.EVENT_TRACE_LOGFILEW.ProcessTraceMode)).ToInt32());
        Assert.Equal(424, Marshal.OffsetOf<EtwNative.EVENT_TRACE_LOGFILEW>(nameof(EtwNative.EVENT_TRACE_LOGFILEW.EventRecordCallback)).ToInt32());

        Assert.Equal(12, Marshal.OffsetOf<EtwNative.EVENT_RECORD>(nameof(EtwNative.EVENT_RECORD.ProcessId)).ToInt32());
        Assert.Equal(40, Marshal.OffsetOf<EtwNative.EVENT_RECORD>(nameof(EtwNative.EVENT_RECORD.EventDescriptorId)).ToInt32());
        Assert.Equal(86, Marshal.OffsetOf<EtwNative.EVENT_RECORD>(nameof(EtwNative.EVENT_RECORD.UserDataLength)).ToInt32());
        Assert.Equal(96, Marshal.OffsetOf<EtwNative.EVENT_RECORD>(nameof(EtwNative.EVENT_RECORD.UserData)).ToInt32());
    }

    [Fact]
    public void EtwProviderGuids_MatchVerifiedWindowsProviders()
    {
        if (!OperatingSystem.IsWindows()) return;

        Assert.Equal(Guid.Parse("7dd42a49-5329-4832-8dfd-43d979153a88"), EtwNetworkListener.KernelNetworkProviderId);
        Assert.Equal(Guid.Parse("1c95126e-7eea-49a9-a3fe-a378b03ddb4d"), EtwDnsListener.DnsClientProviderId);
    }

    [Fact]
    public void EtwDnsListener_ParsesQueryResults_AndMapsIpv4MappedAddresses()
    {
        if (!OperatingSystem.IsWindows()) return;

        using var ms = new MemoryStream();
        ms.Write(Encoding.Unicode.GetBytes("Example.COM.\0"));
        ms.Write(new byte[16]); // QueryType + QueryOptions + Status (or InterfaceIndex + NetworkIndex + Status)
        ms.Write(Encoding.Unicode.GetBytes("type: 5 cdn.example.com;::ffff:93.184.216.34;2606:2800:220:1:248:1893:25c8:1946;\0"));

        bool ok = EtwDnsListener.TryParseDnsEventPayload(ms.ToArray(), out var domain, out var ips);

        Assert.True(ok);
        Assert.Equal("example.com", domain);
        Assert.Contains("93.184.216.34", ips);
        Assert.Contains("2606:2800:220:1:248:1893:25c8:1946", ips);
        Assert.Equal(2, ips.Count);
    }

    [Fact]
    public void NetworkBlockService_TryParseEndpoint_ValidatesFormats()
    {
        if (!OperatingSystem.IsWindows()) return;

        Assert.True(NetworkBlockService.TryParseEndpoint("192.168.1.10:54321", null, out var ip1, out var port1));
        Assert.Equal("192.168.1.10", ip1);
        Assert.Equal(54321, port1);

        Assert.True(NetworkBlockService.TryParseEndpoint("10.0.0.5", 443, out var ip2, out var port2));
        Assert.Equal("10.0.0.5", ip2);
        Assert.Equal(443, port2);

        Assert.False(NetworkBlockService.TryParseEndpoint("*", null, out _, out _));
        Assert.False(NetworkBlockService.TryParseEndpoint("not-an-ip:80", null, out _, out _));
        Assert.False(NetworkBlockService.TryParseEndpoint("1.2.3.4:99999", null, out _, out _));
    }
}
