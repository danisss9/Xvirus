using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Xvirus;
using Xvirus.Model;
using XvirusService.Model;

namespace XvirusService.Services;

/// <summary>
/// Direct network control operations for Xvirus Firewall:
/// blocking a process by PID, blocking a remote IP/CIDR, and forcibly
/// terminating an active IPv4 TCP connection via <c>iphlpapi.dll!SetTcpEntry</c>
/// (<c>MIB_TCP_STATE_DELETE_TCB</c>) without killing the owning process.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class NetworkBlockService(Rules rules)
{
    private const uint MibTcpStateDeleteTcb = 12;

    [StructLayout(LayoutKind.Sequential)]
    private struct MibTcpRow
    {
        public uint State;
        public uint LocalAddr;
        public uint LocalPort;
        public uint RemoteAddr;
        public uint RemotePort;
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint SetTcpEntry(ref MibTcpRow pTcpRow);

    /// <summary>Resolves a PID to its executable path, adds a block rule, and drops its active TCP connections.</summary>
    public NetworkActionResultDTO BlockPid(int pid, bool killConnections = true)
    {
        if (pid <= 4)
        {
            return new NetworkActionResultDTO
            {
                Success = false,
                Message = "Cannot block system or invalid PID."
            };
        }

        var path = ProcessControl.ResolveProcessPath(pid);
        if (string.IsNullOrWhiteSpace(path))
        {
            return new NetworkActionResultDTO
            {
                Success = false,
                Message = $"Could not resolve executable path for PID {pid}."
            };
        }

        var rule = rules.AddBlockRule(path);
        int killed = killConnections ? KillConnectionsForPid(pid) : 0;

        return new NetworkActionResultDTO
        {
            Success = true,
            KilledConnections = killed,
            Rule = rule,
            Message = $"Blocked '{Path.GetFileName(path)}' (dropped {killed} active TCP connection(s))."
        };
    }

    /// <summary>Adds a remote IP/CIDR block rule and optionally drops active TCP connections to that IP.</summary>
    public NetworkActionResultDTO BlockIp(BlockIpRequestDTO request)
    {
        var ip = (request.Ip ?? string.Empty).Trim();
        if (!Firewall.IsValidIpOrCidr(ip))
        {
            return new NetworkActionResultDTO
            {
                Success = false,
                Message = $"Invalid IP address or CIDR: '{ip}'."
            };
        }

        var rule = rules.AddRule(new Rule
        {
            Kind = RuleKind.Ip,
            Path = ip,
            Type = RuleType.Block,
            Protocol = request.Protocol,
            Direction = request.Direction,
            Profile = request.Profile
        });

        int killed = request.KillConnections && !ip.Contains('/') ? KillConnectionsToIp(ip) : 0;

        return new NetworkActionResultDTO
        {
            Success = true,
            KilledConnections = killed,
            Rule = rule,
            Message = $"Blocked IP '{ip}' (dropped {killed} active TCP connection(s))."
        };
    }

    /// <summary>Terminates a single active IPv4 TCP connection using <c>SetTcpEntry</c>.</summary>
    public NetworkActionResultDTO KillConnection(KillConnectionRequestDTO request)
    {
        if (!TryParseEndpoint(request.LocalAddress, request.LocalPort, out var localIp, out var localPort) ||
            !TryParseEndpoint(request.RemoteAddress, request.RemotePort, out var remoteIp, out var remotePort))
        {
            return new NetworkActionResultDTO
            {
                Success = false,
                Message = "Invalid local or remote endpoint."
            };
        }

        bool ok = KillTcpV4(localIp, localPort, remoteIp, remotePort);
        return new NetworkActionResultDTO
        {
            Success = ok,
            KilledConnections = ok ? 1 : 0,
            Message = ok
                ? $"Terminated TCP connection {localIp}:{localPort} -> {remoteIp}:{remotePort}."
                : "Could not terminate connection (only active IPv4 TCP connections can be closed via SetTcpEntry)."
        };
    }

    public int KillConnectionsForPid(int pid)
    {
        int killed = 0;
        try
        {
            foreach (var ep in IpHelper.GetEndpoints())
            {
                if (ep.Pid == pid && ep.Protocol == "TCP" && ep.State == "ESTABLISHED")
                {
                    if (KillTcpV4(ep.LocalAddress, ep.LocalPort, ep.RemoteAddress, ep.RemotePort))
                        killed++;
                }
            }
        }
        catch { /* best effort */ }
        return killed;
    }

    public int KillConnectionsToIp(string remoteIp)
    {
        int killed = 0;
        try
        {
            foreach (var ep in IpHelper.GetEndpoints())
            {
                if (ep.Protocol == "TCP" &&
                    ep.State == "ESTABLISHED" &&
                    string.Equals(ep.RemoteAddress, remoteIp, StringComparison.OrdinalIgnoreCase))
                {
                    if (KillTcpV4(ep.LocalAddress, ep.LocalPort, ep.RemoteAddress, ep.RemotePort))
                        killed++;
                }
            }
        }
        catch { /* best effort */ }
        return killed;
    }

    private static bool KillTcpV4(string localIpStr, int localPort, string remoteIpStr, int remotePort)
    {
        if (!IPAddress.TryParse(localIpStr, out var localIp) ||
            !IPAddress.TryParse(remoteIpStr, out var remoteIp))
            return false;

        if (localIp.AddressFamily != AddressFamily.InterNetwork ||
            remoteIp.AddressFamily != AddressFamily.InterNetwork)
            return false;

        var row = new MibTcpRow
        {
            State = MibTcpStateDeleteTcb,
            LocalAddr = BitConverter.ToUInt32(localIp.GetAddressBytes(), 0),
            LocalPort = Htons(localPort),
            RemoteAddr = BitConverter.ToUInt32(remoteIp.GetAddressBytes(), 0),
            RemotePort = Htons(remotePort),
        };

        uint rc = SetTcpEntry(ref row);
        if (rc == 0)
        {
            Logger.LogHistory("firewall", $"Terminated TCP connection {localIpStr}:{localPort} -> {remoteIpStr}:{remotePort}.");
            return true;
        }
        return false;
    }

    public static bool TryParseEndpoint(string rawAddress, int? explicitPort, out string ip, out int port)
    {
        ip = string.Empty;
        port = 0;
        if (string.IsNullOrWhiteSpace(rawAddress)) return false;

        var trimmed = rawAddress.Trim();
        if (explicitPort is > 0 and <= 65535)
        {
            ip = trimmed;
            port = explicitPort.Value;
            return IPAddress.TryParse(ip, out _);
        }

        int lastColon = trimmed.LastIndexOf(':');
        if (lastColon <= 0 || lastColon == trimmed.Length - 1) return false;

        var hostPart = trimmed[..lastColon].Trim('[', ']');
        var portPart = trimmed[(lastColon + 1)..];
        if (!int.TryParse(portPart, out port) || port is < 1 or > 65535) return false;
        if (!IPAddress.TryParse(hostPart, out _)) return false;

        ip = hostPart;
        return true;
    }

    private static uint Htons(int port)
    {
        ushort p = (ushort)port;
        return (uint)(((p & 0xFF) << 8) | ((p >> 8) & 0xFF));
    }
}
