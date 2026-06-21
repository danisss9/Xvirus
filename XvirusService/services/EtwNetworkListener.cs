using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;

namespace XvirusService.Services;

/// <summary>
/// Real-time ETW subscriber for the <c>Microsoft-Windows-Kernel-Network</c>
/// provider. Fires a callback carrying the owning PID the instant the kernel
/// completes a TCP/UDP operation, so <see cref="NetworkRealTimeProtection"/>
/// can scan a newly-connected process within milliseconds instead of waiting
/// for the next IPHelper snapshot.
///
/// Pure P/Invoke — no <c>Microsoft.Diagnostics.Tracing.TraceEvent</c>
/// dependency, no reflection — so it publishes cleanly under Native AOT.
///
/// The callback receives only the PID (from <c>EVENT_HEADER.ProcessId</c>);
/// connection details are left to the IPHelper poll, which keeps the ETW hot
/// path allocation-free. If the session can't be opened (e.g. not elevated,
/// or the provider is unavailable on this SKU), <see cref="Start"/> returns
/// <c>false</c> and the caller silently falls back to polling alone.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class EtwNetworkListener : IDisposable
{
    // Microsoft-Windows-Kernel-Network provider GUID.
    private static readonly Guid KernelNetworkProviderId =
        new(0x0DD44B21, 0x1A34, 0x4E5A, 0x8F, 0x3E, 0x72, 0xC4, 0x2E, 0x1B, 0x5E, 0x1C);

    private const uint EventControlCodeEnableProvider = 1;
    private const uint WNodeFlagAllMandatory = 0x00000040;
    private const uint EventTraceRealTimeMode = 0x00000100;
    private const int EventTraceControlStop = 1;
    private const uint ErrorSuccess = 0;

    // EVENT_TRACE_PROPERTIES is followed inline by the logger-name and
    // log-file-name wide strings. We allocate one buffer and overlay the struct.
    private const int LoggerNameMaxChars = 256;
    private static readonly int PropertiesSize =
        Marshal.SizeOf<EventTraceProperties>() + LoggerNameMaxChars * 2 * 2; // two WCHAR[] buffers

    private ulong _sessionHandle;
    private ulong _traceHandle;
    private IntPtr _propertiesBuffer = IntPtr.Zero;
    private IntPtr _logfileBuffer = IntPtr.Zero;
    private IntPtr _loggerNamePtr = IntPtr.Zero;
    private Thread? _processThread;
    private volatile bool _running;
    private bool _disposed;

    // Must stay alive for as long as the unmanaged callback pointer is in use.
    // The closure captures `this`, so GetFunctionPointerForDelegate on it
    // yields a function pointer that routes back to the instance.
    private readonly EventRecordCallback _callbackDelegate;
    private readonly Action<int> _onNetworkPid;

    public EtwNetworkListener(Action<int> onNetworkPid)
    {
        _onNetworkPid = onNetworkPid;
        _callbackDelegate = (ref EventRecord record) => HandleRecord(ref record);
    }

    // -----------------------------------------------------------------------
    // Public surface
    // -----------------------------------------------------------------------

    public bool Start(string sessionName)
    {
        if (_running) return true;

        // --- 1. Start the ETW session ---
        _propertiesBuffer = Marshal.AllocHGlobal(PropertiesSize);
        RtlZeroMemory(_propertiesBuffer, (IntPtr)PropertiesSize);

        var props = Marshal.PtrToStructure<EventTraceProperties>(_propertiesBuffer);
        props.Wnode.BufferSize = (uint)PropertiesSize;
        props.Wnode.Flags = WNodeFlagAllMandatory;
        props.Wnode.ClientContext = 1; // QPC clock
        props.LoggerNameOffset = (uint)Marshal.SizeOf<EventTraceProperties>();
        props.LogFileNameOffset = props.LoggerNameOffset + LoggerNameMaxChars * 2;
        Marshal.StructureToPtr(props, _propertiesBuffer, false);

        uint rc = StartTraceW(out _sessionHandle, sessionName, _propertiesBuffer);
        if (rc != ErrorSuccess)
        {
            FreeProperties();
            return false;
        }

        // --- 2. Enable the Kernel-Network provider on the session ---
        Guid providerId = KernelNetworkProviderId; // local copy — EnableTraceEx2 takes ref
        rc = EnableTraceEx2(
            _sessionHandle,
            ref providerId,
            EventControlCodeEnableProvider,
            level: 0,
            matchAnyKeyword: 0,
            matchAllKeyword: 0,
            timeout: 0,
            IntPtr.Zero);
        if (rc != ErrorSuccess)
        {
            ControlTraceW(_sessionHandle, sessionName, _propertiesBuffer, EventTraceControlStop);
            FreeProperties();
            return false;
        }

        // --- 3. Open a real-time consumer stream ---
        // EVENT_TRACE_LOGFILE contains two large embedded structs (EVENT_TRACE
        // and TRACE_LOGFILE_HEADER) that we don't read — we only need
        // LoggerName, ProcessTraceMode, and EventRecordCallback. We allocate a
        // raw buffer, zero it, and write those fields at their documented
        // offsets (computed from the Windows SDK layouts on 64-bit Windows).
        const int LogfileBufferSize = 512; // generous; actual struct is ~472 bytes
        _logfileBuffer = Marshal.AllocHGlobal(LogfileBufferSize);
        RtlZeroMemory(_logfileBuffer, (IntPtr)LogfileBufferSize);

        // LoggerName (offset 8): native wide string with the session name.
        _loggerNamePtr = Marshal.StringToHGlobalUni(sessionName);
        Marshal.WriteIntPtr(_logfileBuffer, OffsetLoggerName, _loggerNamePtr);

        // ProcessTraceMode (offset 28): real-time mode.
        Marshal.WriteInt32(_logfileBuffer, OffsetProcessTraceMode, (int)EventTraceRealTimeMode);

        // EventRecordCallback (offset 440): function pointer for our delegate.
        Marshal.WriteIntPtr(_logfileBuffer, OffsetEventRecordCallback,
            Marshal.GetFunctionPointerForDelegate(_callbackDelegate));

        _traceHandle = OpenTraceW(_logfileBuffer);
        if (_traceHandle == 0 || _traceHandle == ulong.MaxValue)
        {
            ControlTraceW(_sessionHandle, sessionName, _propertiesBuffer, EventTraceControlStop);
            FreeAll();
            return false;
        }

        // --- 4. Start the processing thread (ProcessTrace blocks) ---
        _running = true;
        _processThread = new Thread(() => ProcessTraceLoop(_traceHandle))
        {
            IsBackground = true,
            Name = "EtwNetworkListener.ProcessTrace",
        };
        _processThread.Start();
        return true;
    }

    public void Stop()
    {
        if (!_running) return;
        _running = false;

        // Stopping the session + closing the trace causes ProcessTrace to
        // unblock and return.
        try { ControlTraceW(_sessionHandle, null, _propertiesBuffer, EventTraceControlStop); }
        catch { /* best effort */ }
        try { CloseTrace(_traceHandle); }
        catch { /* best effort */ }

        _processThread?.Join(3000);
    }

    // -----------------------------------------------------------------------
    // Processing thread + callback
    // -----------------------------------------------------------------------

    private void ProcessTraceLoop(ulong traceHandle)
    {
        // ProcessTrace blocks until the session is stopped (Stop() calls
        // ControlTrace + CloseTrace, which unblocks it).
        var handles = new[] { traceHandle };
        try
        {
            ProcessTrace(handles, (uint)handles.Length, IntPtr.Zero, IntPtr.Zero);
        }
        catch
        {
            // Expected when the session is stopped mid-processing.
        }
    }

    private void HandleRecord(ref EventRecord record)
    {
        if (!_running) return;
        int pid = (int)record.EventHeader.ProcessId;
        if (pid > 0)
        {
            try { _onNetworkPid(pid); }
            catch { /* never let a callback error kill the ETW thread */ }
        }
    }

    // -----------------------------------------------------------------------
    // Field offsets in EVENT_TRACE_LOGFILE (64-bit Windows, default packing).
    // Computed from the Windows SDK struct layouts — see the design comment at
    // the top of the class. Only the fields we actually set are listed.
    // -----------------------------------------------------------------------

    private const int OffsetLoggerName = 8;
    private const int OffsetProcessTraceMode = 28;
    private const int OffsetEventRecordCallback = 440;

    // -----------------------------------------------------------------------
    // P/Invoke declarations
    // -----------------------------------------------------------------------

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint StartTraceW(
        out ulong traceHandle,
        string kernelLoggerName,
        IntPtr properties); // EVENT_TRACE_PROPERTIES*

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern uint EnableTraceEx2(
        ulong traceHandle,
        ref Guid providerId,
        uint controlCode,
        byte level,
        ulong matchAnyKeyword,
        ulong matchAllKeyword,
        uint timeout,
        IntPtr enableParameters);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint ControlTraceW(
        ulong traceHandle,
        string? sessionName,
        IntPtr properties,
        uint controlCode);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern uint CloseTrace(ulong traceHandle);

    // OpenTraceW takes a pointer to EVENT_TRACE_LOGFILE. We pass a raw buffer
    // (see Start) so we don't need to define the full struct here.
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern ulong OpenTraceW(IntPtr logfile);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern uint ProcessTrace(
        [In] ulong[] traceHandles,
        uint handleCount,
        IntPtr startTime,
        IntPtr endTime);

    [DllImport("kernel32.dll")]
    private static extern void RtlZeroMemory(IntPtr destination, IntPtr length);

    // -----------------------------------------------------------------------
    // Structs (layouts match the Windows SDK; do not reorder)
    // -----------------------------------------------------------------------

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void EventRecordCallback(ref EventRecord record);

    [StructLayout(LayoutKind.Sequential)]
    private struct WnodeHeader
    {
        public uint BufferSize;
        public uint ProviderId;
        public ulong HistoricalContext;
        public ulong TimeStamp;
        public Guid Guid;
        public uint ClientContext;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EventTraceProperties
    {
        public WnodeHeader Wnode;
        public uint BufferSize;
        public uint MinimumBuffers;
        public uint MaximumBuffers;
        public uint MaximumFileSize;
        public uint LogFileMode;
        public uint FlushTimer;
        public uint EnableFlags;
        public IntPtr Extension; // reserved
        public uint LogFileNameOffset;
        public uint LoggerNameOffset;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EventHeader
    {
        public ushort Size;
        public ushort HeaderType;
        public ushort Flags;
        public ushort EventProperty;
        public uint ThreadId;
        public uint ProcessId;
        public long TimeStamp;
        public Guid ProviderId;
        public ushort Id;
        public ushort Version;
        public ushort Channel;
        public ushort Level;
        public ushort Opcode;
        public ushort Task;
        public ulong Keyword;
        public uint KernelTime;
        public uint UserTime;
        public Guid ActivityId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EventRecord
    {
        public EventHeader EventHeader;
        public uint ExtendedDataCount;
        public uint UserDataLength;
        public IntPtr ExtendedData; // PEVENT_HEADER_EXTENDED_DATA_ITEM
        public IntPtr UserData;
    }

    // -----------------------------------------------------------------------

    // -----------------------------------------------------------------------
    // Cleanup
    // -----------------------------------------------------------------------

    private void FreeProperties()
    {
        if (_propertiesBuffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_propertiesBuffer);
            _propertiesBuffer = IntPtr.Zero;
        }
    }

    private void FreeAll()
    {
        FreeProperties();
        if (_loggerNamePtr != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_loggerNamePtr);
            _loggerNamePtr = IntPtr.Zero;
        }
        if (_logfileBuffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_logfileBuffer);
            _logfileBuffer = IntPtr.Zero;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        FreeAll();
    }
}
