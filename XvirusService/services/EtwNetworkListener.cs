using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace XvirusService.Services;

/// <summary>
/// Real-time ETW subscriber for the <c>Microsoft-Windows-Kernel-Network</c>
/// provider (<c>{7DD42A49-5329-4832-8DFD-43D979153A88}</c>). Fires a callback
/// carrying the owning PID the instant the kernel completes a TCP/UDP operation,
/// so <see cref="NetworkRealTimeProtection"/> can scan a newly-connected process
/// within milliseconds instead of waiting for the next IPHelper snapshot.
///
/// Built on <see cref="EtwNative"/> with <c>[UnmanagedCallersOnly]</c> so it
/// publishes cleanly under Native AOT.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class EtwNetworkListener : IDisposable
{
    // Verified via `logman query providers` / `wevtutil gp Microsoft-Windows-Kernel-Network`
    public static readonly Guid KernelNetworkProviderId =
        new("7dd42a49-5329-4832-8dfd-43d979153a88");

    // KERNEL_NETWORK_KEYWORD_IPV4 (0x10) | KERNEL_NETWORK_KEYWORD_IPV6 (0x20)
    private const ulong KeywordIpV4AndV6 = 0x30;

    // Singleton ref for the unmanaged callback — one network listener per process.
    private static EtwNetworkListener? _current;

    private readonly Action<int> _onNetworkPid;
    private readonly object _lock = new();
    private string _sessionName = string.Empty;
    private ulong _sessionHandle;
    private long _traceHandle = EtwNative.InvalidHandle;
    private Thread? _processThread;
    private volatile bool _running;
    private bool _disposed;

    public EtwNetworkListener(Action<int> onNetworkPid)
    {
        _onNetworkPid = onNetworkPid;
    }

    public unsafe bool Start(string sessionName)
    {
        lock (_lock)
        {
            if (_running) return true;
            _sessionName = sessionName;

            ulong sessionHandle = EtwNative.StartRealTimeSession(sessionName, out uint err);
            if (sessionHandle == 0)
            {
                Console.WriteLine($"EtwNetworkListener: StartTraceW failed ({err}).");
                return false;
            }

            Guid providerId = KernelNetworkProviderId;
            uint enableRc = EtwNative.EnableTraceEx2(
                sessionHandle,
                &providerId,
                EtwNative.EVENT_CONTROL_CODE_ENABLE_PROVIDER,
                EtwNative.TRACE_LEVEL_INFORMATION,
                KeywordIpV4AndV6,
                0,
                0,
                null);

            if (enableRc != EtwNative.ERROR_SUCCESS)
            {
                Console.WriteLine($"EtwNetworkListener: EnableTraceEx2 failed ({enableRc}).");
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
                Console.WriteLine($"EtwNetworkListener: OpenTraceW failed ({Marshal.GetLastWin32Error()}).");
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
                Name = "EtwNetworkListener.ProcessTrace",
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

        lock (_lock)
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

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static unsafe void OnEventRecord(EtwNative.EVENT_RECORD* record)
    {
        try
        {
            var inst = _current;
            if (inst == null || !inst._running) return;

            // For Microsoft-Windows-Kernel-Network (TCPIP / UDPIP events),
            // EventHeader.ProcessId is often 0xFFFFFFFF (kernel/DPC context);
            // the owning process ID is stored as uint32 at UserData offset 0.
            int pid = 0;
            if (record->UserData != null && record->UserDataLength >= 4)
            {
                uint userPid = *(uint*)record->UserData;
                if (userPid > 4 && userPid < int.MaxValue)
                    pid = (int)userPid;
            }

            if (pid <= 0 && record->ProcessId > 4 && record->ProcessId < int.MaxValue)
                pid = (int)record->ProcessId;

            if (pid > 4)
                inst._onNetworkPid(pid);
        }
        catch { /* never throw across unmanaged boundary */ }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        if (ReferenceEquals(_current, this)) _current = null;
    }
}
