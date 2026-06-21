using System.Net;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace XvirusService.Services;

/// <summary>
/// Userland TCP/UDP table enumeration via the IPHelper API
/// (<c>GetExtendedTcpTable</c> / <c>GetExtendedUdpTable</c>). Replaces the
/// 3-second <c>netstat -ano</c> poll with a sub-millisecond in-process call so
/// the network monitor can run on a 500ms–1s timer without spawning a process.
///
/// Only the IPv4 + IPv6 rows we actually consume are mapped. The structs are
/// laid out exactly as Windows returns them; do not reorder fields.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class IpHelper
{
    // TCP states (MIB_TCP_STATE_*). We only care about a handful.
    private const int TcpStateListen = 2;
    private const int TcpStateSynSent = 3;
    private const int TcpStateSynRcvd = 4;
    private const int TcpStateEstab = 5;
    private const int TcpStateFinWait1 = 6;
    private const int TcpStateFinWait2 = 7;
    private const int TcpStateCloseWait = 8;
    private const int TcpStateClosing = 9;
    private const int TcpStateLastAck = 10;
    private const int TcpStateTimeWait = 11;
    private const int TcpStateDeleteTcb = 12;

    private const int TcpTableOwnerPidAll = 5;   // MIB_TCP_TABLE_OWNER_PID
    private const int UdpTableOwnerPid = 1;      // MIB_UDP_TABLE_OWNER_PID

    private const int AfInet = 2;     // AF_INET
    private const int AfInet6 = 23;   // AF_INET6

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(
        IntPtr pTcpTable, ref uint pdwSize, bool bOrder, uint ulAf, uint TableClass, uint Reserved);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedUdpTable(
        IntPtr pUdpTable, ref uint pdwSize, bool bOrder, uint ulAf, uint TableClass, uint Reserved);

    /// <summary>Wraps a Get*Table P/Invoke so <see cref="QueryTable"/> can pass the size by ref.</summary>
    private delegate uint TableQuery(IntPtr buffer, ref uint size);

    // -----------------------------------------------------------------------
    // Public surface
    // -----------------------------------------------------------------------

    /// <summary>Snapshot of every TCP + UDP (v4 and v6) endpoint with an owning PID.</summary>
    public static List<NetworkEndpoint> GetEndpoints()
    {
        var result = new List<NetworkEndpoint>(256);
        result.AddRange(GetTcpV4());
        result.AddRange(GetTcpV6());
        result.AddRange(GetUdpV4());
        result.AddRange(GetUdpV6());
        return result;
    }

    /// <summary>Distinct PIDs that currently own at least one TCP/UDP endpoint.</summary>
    public static HashSet<int> GetConnectedPids()
    {
        var pids = new HashSet<int>();
        foreach (var ep in GetEndpoints())
        {
            if (ep.Pid > 0) pids.Add(ep.Pid);
        }
        return pids;
    }

    // -----------------------------------------------------------------------
    // TCP v4
    // -----------------------------------------------------------------------

    [StructLayout(LayoutKind.Sequential)]
    private struct MibTcpRowOwnerPid
    {
        public uint State;
        public uint LocalAddr;
        public uint LocalPort;
        public uint RemoteAddr;
        public uint RemotePort;
        public uint OwningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MibTcpTableOwnerPid
    {
        public uint NumEntries;
        // Followed by NumEntries rows inline.
    }

    private static List<NetworkEndpoint> GetTcpV4()
    {
        var result = new List<NetworkEndpoint>(64);
        QueryTable(
            (IntPtr buf, ref uint size) => GetExtendedTcpTable(buf, ref size, false, AfInet, TcpTableOwnerPidAll, 0),
            rowSize: 24,
            row =>
            {
                var r = Marshal.PtrToStructure<MibTcpRowOwnerPid>(row);
                result.Add(new NetworkEndpoint
                {
                    Protocol = "TCP",
                    LocalAddress = Ipv4ToString(r.LocalAddr),
                    LocalPort = ntohs(r.LocalPort),
                    RemoteAddress = Ipv4ToString(r.RemoteAddr),
                    RemotePort = ntohs(r.RemotePort),
                    State = TcpStateName(r.State),
                    Pid = (int)r.OwningPid,
                });
            });
        return result;
    }

    // -----------------------------------------------------------------------
    // TCP v6
    // -----------------------------------------------------------------------

    [StructLayout(LayoutKind.Sequential)]
    private struct MibTcp6RowOwnerPid
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] LocalAddr;
        public uint LocalScopeId;
        public uint LocalPort;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] RemoteAddr;
        public uint RemoteScopeId;
        public uint RemotePort;
        public uint State;
        public uint OwningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MibTcp6TableOwnerPid
    {
        public uint NumEntries;
    }

    private static List<NetworkEndpoint> GetTcpV6()
    {
        var result = new List<NetworkEndpoint>(64);
        QueryTable(
            (IntPtr buf, ref uint size) => GetExtendedTcpTable(buf, ref size, false, AfInet6, TcpTableOwnerPidAll, 0),
            rowSize: 56,
            row =>
            {
                var r = Marshal.PtrToStructure<MibTcp6RowOwnerPid>(row);
                result.Add(new NetworkEndpoint
                {
                    Protocol = "TCP6",
                    LocalAddress = Ipv6ToString(r.LocalAddr, r.LocalScopeId),
                    LocalPort = ntohs(r.LocalPort),
                    RemoteAddress = Ipv6ToString(r.RemoteAddr, r.RemoteScopeId),
                    RemotePort = ntohs(r.RemotePort),
                    State = TcpStateName(r.State),
                    Pid = (int)r.OwningPid,
                });
            });
        return result;
    }

    // -----------------------------------------------------------------------
    // UDP v4
    // -----------------------------------------------------------------------

    [StructLayout(LayoutKind.Sequential)]
    private struct MibUdpRowOwnerPid
    {
        public uint LocalAddr;
        public uint LocalPort;
        public uint OwningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MibUdpTableOwnerPid
    {
        public uint NumEntries;
    }

    private static List<NetworkEndpoint> GetUdpV4()
    {
        var result = new List<NetworkEndpoint>(64);
        QueryTable(
            (IntPtr buf, ref uint size) => GetExtendedUdpTable(buf, ref size, false, AfInet, UdpTableOwnerPid, 0),
            rowSize: 12,
            row =>
            {
                var r = Marshal.PtrToStructure<MibUdpRowOwnerPid>(row);
                result.Add(new NetworkEndpoint
                {
                    Protocol = "UDP",
                    LocalAddress = Ipv4ToString(r.LocalAddr),
                    LocalPort = ntohs(r.LocalPort),
                    RemoteAddress = "*",
                    RemotePort = 0,
                    State = string.Empty,
                    Pid = (int)r.OwningPid,
                });
            });
        return result;
    }

    // -----------------------------------------------------------------------
    // UDP v6
    // -----------------------------------------------------------------------

    [StructLayout(LayoutKind.Sequential)]
    private struct MibUdp6RowOwnerPid
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] LocalAddr;
        public uint LocalScopeId;
        public uint LocalPort;
        public uint OwningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MibUdp6TableOwnerPid
    {
        public uint NumEntries;
    }

    private static List<NetworkEndpoint> GetUdpV6()
    {
        var result = new List<NetworkEndpoint>(64);
        QueryTable(
            (IntPtr buf, ref uint size) => GetExtendedUdpTable(buf, ref size, false, AfInet6, UdpTableOwnerPid, 0),
            rowSize: 28,
            row =>
            {
                var r = Marshal.PtrToStructure<MibUdp6RowOwnerPid>(row);
                result.Add(new NetworkEndpoint
                {
                    Protocol = "UDP6",
                    LocalAddress = Ipv6ToString(r.LocalAddr, r.LocalScopeId),
                    LocalPort = ntohs(r.LocalPort),
                    RemoteAddress = "*",
                    RemotePort = 0,
                    State = string.Empty,
                    Pid = (int)r.OwningPid,
                });
            });
        return result;
    }

    // -----------------------------------------------------------------------
    // Shared plumbing
    // -----------------------------------------------------------------------

    /// <summary>
    /// Calls a Get*Table function, growing the buffer as needed, and invokes
    /// <paramref name="onRow"/> for each row while the unmanaged buffer is still
    /// alive. The buffer is freed before returning, so callers must marshal
    /// everything they need inside <paramref name="onRow"/>.
    /// </summary>
    private static void QueryTable(TableQuery query, int rowSize, Action<IntPtr> onRow)
    {
        uint size = 0;
        // First call: discover required size.
        query(IntPtr.Zero, ref size);

        const int headerSize = 4; // uint NumEntries
        IntPtr buffer = IntPtr.Zero;
        try
        {
            uint needed = size;
            if (needed == 0) needed = (uint)(headerSize + 64 * rowSize);
            buffer = Marshal.AllocHGlobal((int)needed);
            size = needed;

            uint rc = query(buffer, ref size);
            if (rc == 122) // ERROR_INSUFFICIENT_BUFFER — size changed underneath us
            {
                Marshal.FreeHGlobal(buffer);
                buffer = Marshal.AllocHGlobal((int)size);
                rc = query(buffer, ref size);
            }
            if (rc != 0) return; // No data or failure — leave empty.

            uint numEntries = (uint)Marshal.ReadInt32(buffer);
            IntPtr rowPtr = buffer + headerSize;
            for (int i = 0; i < numEntries; i++)
            {
                onRow(rowPtr);
                rowPtr += rowSize;
            }
        }
        finally
        {
            if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
        }
    }

    // -----------------------------------------------------------------------
    // Formatting helpers
    // -----------------------------------------------------------------------

    private static string Ipv4ToString(uint addr)
    {
        // Network byte order → display order.
        long a = addr & 0xFF;
        long b = (addr >> 8) & 0xFF;
        long c = (addr >> 16) & 0xFF;
        long d = (addr >> 24) & 0xFF;
        return $"{a}.{b}.{c}.{d}";
    }

    private static string Ipv6ToString(byte[] addr, uint scopeId)
    {
        try
        {
            var bytes = new byte[16];
            Array.Copy(addr, bytes, 16);
            var ip = new IPAddress(bytes, scopeId);
            return ip.ToString();
        }
        catch
        {
            return "::";
        }
    }

    private static ushort ntohs(uint port)
    {
        // IPHelper stores ports in network byte order; swap to host order.
        return (ushort)(((port & 0xFF) << 8) | ((port >> 8) & 0xFF));
    }

    private static string TcpStateName(uint state) => state switch
    {
        TcpStateListen => "LISTEN",
        TcpStateSynSent => "SYN_SENT",
        TcpStateSynRcvd => "SYN_RCVD",
        TcpStateEstab => "ESTABLISHED",
        TcpStateFinWait1 => "FIN_WAIT1",
        TcpStateFinWait2 => "FIN_WAIT2",
        TcpStateCloseWait => "CLOSE_WAIT",
        TcpStateClosing => "CLOSING",
        TcpStateLastAck => "LAST_ACK",
        TcpStateTimeWait => "TIME_WAIT",
        TcpStateDeleteTcb => "DELETE_TCB",
        _ => state == 1 ? "CLOSED" : "UNKNOWN",
    };
}

/// <summary>One TCP/UDP endpoint with owning PID, in display-ready form.</summary>
internal sealed class NetworkEndpoint
{
    public string Protocol { get; set; } = string.Empty;
    public string LocalAddress { get; set; } = string.Empty;
    public int LocalPort { get; set; }
    public string RemoteAddress { get; set; } = string.Empty;
    public int RemotePort { get; set; }
    public string State { get; set; } = string.Empty;
    public int Pid { get; set; }
}
