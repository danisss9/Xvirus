using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Xvirus;

namespace XvirusService.Services;

/// <summary>
/// Real-time ETW subscriber for <c>Microsoft-Windows-DNS-Client</c>
/// (<c>{1C95126E-7EEA-49A9-A3FE-A378B03DDB4D}</c>). Captures DNS resolution
/// results (Event IDs 3008, 3018, 3020) to:
/// 1. Enrich Network Monitor rows with the resolved remote domain name for an IP.
/// 2. Enforce <see cref="Xvirus.Model.RuleKind.Domain"/> block rules by dynamically
///    blocking resolved IP addresses via <see cref="Rules.EnforceDomainResolution"/>.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class EtwDnsListener : IDisposable
{
    // Verified via `wevtutil gp Microsoft-Windows-DNS-Client`
    public static readonly Guid DnsClientProviderId =
        new("1c95126e-7eea-49a9-a3fe-a378b03ddb4d");

    private const int MaxCacheEntries = 4096;

    private static EtwDnsListener? _current;

    private readonly Rules _rules;
    private readonly ConcurrentDictionary<string, string> _ipToDomain = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, HashSet<string>> _domainToIps = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _domainMapLock = new();
    private readonly object _lifecycleLock = new();

    private string _sessionName = string.Empty;
    private ulong _sessionHandle;
    private long _traceHandle = EtwNative.InvalidHandle;
    private Thread? _processThread;
    private volatile bool _running;
    private bool _disposed;

    public EtwDnsListener(Rules rules)
    {
        _rules = rules;
    }

    public unsafe bool Start(string sessionName)
    {
        lock (_lifecycleLock)
        {
            if (_running) return true;
            _sessionName = sessionName;

            ulong sessionHandle = EtwNative.StartRealTimeSession(sessionName, out uint err);
            if (sessionHandle == 0)
            {
                Console.WriteLine($"EtwDnsListener: StartTraceW failed ({err}).");
                return false;
            }

            Guid providerId = DnsClientProviderId;
            uint enableRc = EtwNative.EnableTraceEx2(
                sessionHandle,
                &providerId,
                EtwNative.EVENT_CONTROL_CODE_ENABLE_PROVIDER,
                EtwNative.TRACE_LEVEL_INFORMATION,
                ulong.MaxValue,
                0,
                0,
                null);

            if (enableRc != EtwNative.ERROR_SUCCESS)
            {
                Console.WriteLine($"EtwDnsListener: EnableTraceEx2 failed ({enableRc}).");
                EtwNative.StopSession(sessionHandle, sessionName);
                return false;
            }

            _current = this;
            var logFile = new EtwNative.EVENT_TRACE_LOGFILEW
            {
                ProcessTraceMode = EtwNative.PROCESS_TRACE_MODE_REAL_TIME | EtwNative.PROCESS_TRACE_MODE_EVENT_RECORD,
                EventRecordCallback = &OnEventRecord,
            };

            long traceHandle;
            fixed (char* name = sessionName)
            {
                logFile.LoggerName = name;
                traceHandle = EtwNative.OpenTraceW(&logFile);
            }

            if (traceHandle == EtwNative.InvalidHandle)
            {
                Console.WriteLine($"EtwDnsListener: OpenTraceW failed ({Marshal.GetLastWin32Error()}).");
                EtwNative.StopSession(sessionHandle, sessionName);
                return false;
            }

            _sessionHandle = sessionHandle;
            _traceHandle = traceHandle;
            _running = true;

            _processThread = new Thread(static () =>
            {
                var inst = _current;
                if (inst == null) return;
                long h = inst._traceHandle;
                if (h != EtwNative.InvalidHandle)
                    EtwNative.ProcessTrace(&h, 1, null, null);
            })
            {
                IsBackground = true,
                Name = "EtwDnsListener.ProcessTrace",
            };
            _processThread.Start();
            return true;
        }
    }

    public void Stop()
    {
        long th;
        ulong sh;
        string name;
        Thread? thread;

        lock (_lifecycleLock)
        {
            if (!_running) return;
            _running = false;
            th = _traceHandle;
            _traceHandle = EtwNative.InvalidHandle;
            sh = _sessionHandle;
            _sessionHandle = 0;
            name = _sessionName;
            thread = _processThread;
            _processThread = null;
        }

        if (th != EtwNative.InvalidHandle)
            EtwNative.CloseTrace((ulong)th);

        if (sh != 0)
            EtwNative.StopSession(sh, name);

        thread?.Join(2000);
    }

    /// <summary>Looks up the most recently resolved domain name for an IP address.</summary>
    public string TryGetDomain(string ip)
    {
        if (string.IsNullOrWhiteSpace(ip)) return string.Empty;
        return _ipToDomain.TryGetValue(ip.Trim(), out var domain) ? domain : string.Empty;
    }

    /// <summary>
    /// Enforces a newly added domain block rule immediately against both cached DNS
    /// resolutions and a live DNS lookup so the block takes effect without waiting for
    /// another DNS query.
    /// </summary>
    public async Task EnforceDomainNowAsync(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain)) return;
        var clean = domain.Trim().TrimEnd('.').ToLowerInvariant();

        // 1. Enforce any IPs already seen in the ETW DNS cache.
        List<string> cached = new();
        lock (_domainMapLock)
        {
            foreach (var (d, ips) in _domainToIps)
            {
                if (d.Equals(clean, StringComparison.OrdinalIgnoreCase) ||
                    d.EndsWith("." + clean, StringComparison.OrdinalIgnoreCase))
                {
                    cached.AddRange(ips);
                }
            }
        }
        foreach (var ip in cached)
            _rules.EnforceDomainResolution(clean, ip);

        // 2. Best-effort forward lookup for the domain's current A/AAAA records.
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(clean);
            foreach (var addr in addresses)
            {
                var ipStr = NormalizeIp(addr.ToString());
                if (ipStr != null)
                {
                    RecordMapping(clean, ipStr);
                    _rules.EnforceDomainResolution(clean, ipStr);
                }
            }
        }
        catch { /* offline or NXDOMAIN — ETW will catch future resolutions */ }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static unsafe void OnEventRecord(EtwNative.EVENT_RECORD* record)
    {
        try
        {
            var inst = _current;
            if (inst == null || !inst._running) return;

            ushort id = record->EventDescriptorId;
            if (id is not (3008 or 3018 or 3020)) return;
            if (record->UserData == null || record->UserDataLength < 20) return;

            var span = new ReadOnlySpan<byte>(record->UserData, record->UserDataLength);
            if (!TryParseDnsEventPayload(span, out var domain, out var ips) || ips.Count == 0)
                return;

            inst.HandleResolution(domain, ips);
        }
        catch { /* never throw from unmanaged callback */ }
    }

    private void HandleResolution(string domain, List<string> ips)
    {
        foreach (var ip in ips)
        {
            RecordMapping(domain, ip);
            // Enforce domain block rules off the ETW consumer thread so netsh never stalls ETW.
            var d = domain;
            var addr = ip;
            _ = Task.Run(() => _rules.EnforceDomainResolution(d, addr));
        }
    }

    private void RecordMapping(string domain, string ip)
    {
        if (_ipToDomain.Count >= MaxCacheEntries)
            _ipToDomain.Clear();

        _ipToDomain[ip] = domain;

        lock (_domainMapLock)
        {
            if (_domainToIps.Count >= MaxCacheEntries)
                _domainToIps.Clear();

            if (!_domainToIps.TryGetValue(domain, out var set))
            {
                set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _domainToIps[domain] = set;
            }
            set.Add(ip);
        }
    }

    /// <summary>
    /// Parses the UTF-16 <c>QueryName</c> and <c>QueryResults</c> fields from
    /// <c>Microsoft-Windows-DNS-Client</c> Event 3008 / 3018 / 3020 payloads.
    /// In all three events, <c>QueryName</c> starts at offset 0 (null-terminated UTF-16),
    /// followed by 16 bytes of scalar fields, followed by <c>QueryResults</c>
    /// (null-terminated UTF-16 semicolon-separated records).
    /// </summary>
    public static bool TryParseDnsEventPayload(ReadOnlySpan<byte> userData, out string domain, out List<string> ips)
    {
        domain = string.Empty;
        ips = new List<string>();

        if (!TryReadUtf16Z(userData, 0, out var rawDomain, out int nextOffset))
            return false;

        domain = rawDomain.Trim().TrimEnd('.').ToLowerInvariant();
        if (string.IsNullOrEmpty(domain))
            return false;

        // 16 scalar bytes sit between QueryName\0 and QueryResults\0 in 3008, 3018, and 3020.
        int resultsOffset = nextOffset + 16;
        if (resultsOffset >= userData.Length)
            return false;

        if (!TryReadUtf16Z(userData, resultsOffset, out var rawResults, out _))
            return false;

        if (string.IsNullOrWhiteSpace(rawResults))
            return false;

        foreach (var token in rawResults.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            // Entries can look like "::ffff:142.250.74.78", "2607:f8b0::200e", or "type: 5 foo.bar"
            var candidate = token;
            if (candidate.StartsWith("type:", StringComparison.OrdinalIgnoreCase))
            {
                int lastSpace = candidate.LastIndexOf(' ');
                if (lastSpace < 0) continue;
                candidate = candidate[(lastSpace + 1)..].Trim();
            }

            var ip = NormalizeIp(candidate);
            if (ip != null && !ips.Contains(ip, StringComparer.OrdinalIgnoreCase))
                ips.Add(ip);
        }

        return ips.Count > 0;
    }

    private static string? NormalizeIp(string candidate)
    {
        if (!IPAddress.TryParse(candidate, out var addr))
            return null;

        if (addr.IsIPv4MappedToIPv6)
            addr = addr.MapToIPv4();

        if (addr.AddressFamily is not (AddressFamily.InterNetwork or AddressFamily.InterNetworkV6))
            return null;

        return addr.ToString();
    }

    private static bool TryReadUtf16Z(ReadOnlySpan<byte> data, int startOffset, out string value, out int nextOffset)
    {
        value = string.Empty;
        nextOffset = startOffset;
        if (startOffset < 0 || startOffset + 2 > data.Length)
            return false;

        int pos = startOffset;
        while (pos + 1 < data.Length)
        {
            if (data[pos] == 0 && data[pos + 1] == 0)
            {
                value = Encoding.Unicode.GetString(data.Slice(startOffset, pos - startOffset));
                nextOffset = pos + 2;
                return true;
            }
            pos += 2;
        }
        return false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        if (ReferenceEquals(_current, this)) _current = null;
    }
}
