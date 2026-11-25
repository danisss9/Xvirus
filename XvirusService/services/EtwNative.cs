using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace XvirusService.Services;

/// <summary>
/// Shared, Native AOT–safe ETW P/Invoke declarations and struct layouts for
/// real-time consumers (<see cref="RealTimeProtection"/>, <see cref="EtwNetworkListener"/>,
/// and <see cref="EtwDnsListener"/>). All struct sizes and explicit field offsets
/// match the 64-bit Windows SDK headers (<c>evntrace.h</c> / <c>evntcons.h</c>).
/// </summary>
[SupportedOSPlatform("windows")]
public static unsafe partial class EtwNative
{
    public const long InvalidHandle = unchecked((long)0xFFFFFFFFFFFFFFFF);
    public const uint ERROR_SUCCESS = 0;
    public const uint ERROR_ALREADY_EXISTS = 183;
    public const uint WNODE_FLAG_TRACED_GUID = 0x00020000;
    public const uint EVENT_TRACE_REAL_TIME_MODE = 0x00000100;
    public const uint EVENT_TRACE_CONTROL_STOP = 1;
    public const uint EVENT_CONTROL_CODE_ENABLE_PROVIDER = 1;
    public const uint PROCESS_TRACE_MODE_REAL_TIME = 0x00000100;
    public const uint PROCESS_TRACE_MODE_EVENT_RECORD = 0x10000000;
    public const byte TRACE_LEVEL_INFORMATION = 4;

    /// <summary>WNODE_HEADER (48 bytes on x64).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct WNODE_HEADER
    {
        public uint BufferSize;          // 0
        public uint ProviderId;          // 4
        public ulong HistoricalContext;  // 8
        public long TimeStamp;           // 16
        public Guid Guid;                // 24
        public uint ClientContext;       // 40
        public uint Flags;               // 44
    }

    /// <summary>EVENT_TRACE_PROPERTIES (120 bytes on x64).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct EVENT_TRACE_PROPERTIES
    {
        public WNODE_HEADER Wnode;             // 0
        public uint BufferSize;                // 48
        public uint MinimumBuffers;            // 52
        public uint MaximumBuffers;            // 56
        public uint MaximumFileSize;           // 60
        public uint LogFileMode;               // 64
        public uint FlushTimer;                // 68
        public uint EnableFlags;               // 72
        public int AgeLimit;                   // 76
        public uint NumberOfBuffers;           // 80
        public uint FreeBuffers;               // 84
        public uint EventsLost;                // 88
        public uint BuffersWritten;            // 92
        public uint LogBuffersLost;            // 96
        public uint RealTimeBuffersLost;       // 100
        public IntPtr LoggerThreadId;          // 104
        public uint LogFileNameOffset;         // 112
        public uint LoggerNameOffset;          // 116
    }

    /// <summary>
    /// EVENT_TRACE_LOGFILEW — explicit offsets for fields we configure.
    /// Full size on x64: 448 bytes. The EventRecordCallback union is at offset 424.
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 448)]
    public struct EVENT_TRACE_LOGFILEW
    {
        [FieldOffset(0)] public char* LogFileName;
        [FieldOffset(8)] public char* LoggerName;
        [FieldOffset(28)] public uint ProcessTraceMode;
        [FieldOffset(424)] public delegate* unmanaged[Stdcall]<EVENT_RECORD*, void> EventRecordCallback;
    }

    /// <summary>
    /// EVENT_RECORD — explicit offsets for the fields we inspect.
    /// ProcessId at 12, EventDescriptor.Id at 40, UserDataLength at 86, UserData at 96.
    /// </summary>
    [StructLayout(LayoutKind.Explicit)]
    public struct EVENT_RECORD
    {
        // EVENT_HEADER starts at 0: Size(2), HeaderType(2), Flags(2), EventProperty(2), ThreadId(4), ProcessId(4)
        [FieldOffset(12)] public uint ProcessId;

        // EVENT_DESCRIPTOR starts at offset 40 inside EVENT_HEADER; Id is its first ushort.
        [FieldOffset(40)] public ushort EventDescriptorId;

        // ETW_BUFFER_CONTEXT (4) at 80, ExtendedDataCount (2) at 84, UserDataLength (2) at 86
        [FieldOffset(86)] public ushort UserDataLength;

        // ExtendedData ptr (8) at 88, UserData ptr (8) at 96
        [FieldOffset(96)] public void* UserData;
    }

    // -----------------------------------------------------------------------
    // Session lifecycle helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Starts a real-time ETW trace session, reclaiming any stale session with the
    /// same name left behind by an earlier crash. Returns <c>0</c> on failure.
    /// </summary>
    public static ulong StartRealTimeSession(string sessionName, out uint errorCode)
    {
        int nameBytes = (sessionName.Length + 1) * 2;
        int totalSize = sizeof(EVENT_TRACE_PROPERTIES) + nameBytes;

        IntPtr buf = Marshal.AllocHGlobal(totalSize);
        try
        {
            InitProperties(buf, totalSize);
            ulong handle = 0;
            uint err = StartTraceW(&handle, sessionName, (EVENT_TRACE_PROPERTIES*)buf);

            if (err == ERROR_ALREADY_EXISTS)
            {
                ControlTraceW(0, sessionName, (EVENT_TRACE_PROPERTIES*)buf, EVENT_TRACE_CONTROL_STOP);
                InitProperties(buf, totalSize);
                err = StartTraceW(&handle, sessionName, (EVENT_TRACE_PROPERTIES*)buf);
            }

            errorCode = err;
            return err == ERROR_SUCCESS ? handle : 0;
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }

    public static void StopSession(ulong sessionHandle, string sessionName)
    {
        if (sessionHandle == 0) return;

        int nameBytes = (sessionName.Length + 1) * 2;
        int totalSize = sizeof(EVENT_TRACE_PROPERTIES) + nameBytes;
        IntPtr buf = Marshal.AllocHGlobal(totalSize);
        try
        {
            new Span<byte>((void*)buf, totalSize).Clear();
            var p = (EVENT_TRACE_PROPERTIES*)buf;
            p->Wnode.BufferSize = (uint)totalSize;
            p->Wnode.Flags = WNODE_FLAG_TRACED_GUID;
            ControlTraceW(sessionHandle, null, p, EVENT_TRACE_CONTROL_STOP);
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }

    private static void InitProperties(IntPtr buf, int totalSize)
    {
        new Span<byte>((void*)buf, totalSize).Clear();
        var p = (EVENT_TRACE_PROPERTIES*)buf;
        p->Wnode.BufferSize = (uint)totalSize;
        p->Wnode.Flags = WNODE_FLAG_TRACED_GUID;
        p->Wnode.ClientContext = 1; // QPC clock
        p->LogFileMode = EVENT_TRACE_REAL_TIME_MODE;
        p->LoggerNameOffset = (uint)sizeof(EVENT_TRACE_PROPERTIES);
    }

    // -----------------------------------------------------------------------
    // P/Invoke declarations (Native AOT friendly via LibraryImport)
    // -----------------------------------------------------------------------

    [LibraryImport("advapi32.dll", EntryPoint = "StartTraceW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint StartTraceW(
        ulong* traceHandle,
        string instanceName,
        EVENT_TRACE_PROPERTIES* properties);

    [LibraryImport("advapi32.dll", EntryPoint = "ControlTraceW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint ControlTraceW(
        ulong traceHandle,
        [MarshalAs(UnmanagedType.LPWStr)] string? instanceName,
        EVENT_TRACE_PROPERTIES* properties,
        uint controlCode);

    [LibraryImport("advapi32.dll", EntryPoint = "EnableTraceEx2")]
    public static partial uint EnableTraceEx2(
        ulong traceHandle,
        Guid* providerId,
        uint controlCode,
        byte level,
        ulong matchAnyKeyword,
        ulong matchAllKeyword,
        uint timeout,
        void* enableParameters);

    [LibraryImport("advapi32.dll", EntryPoint = "OpenTraceW")]
    public static partial long OpenTraceW(EVENT_TRACE_LOGFILEW* logfile);

    [LibraryImport("advapi32.dll", EntryPoint = "ProcessTrace")]
    public static partial uint ProcessTrace(
        long* handleArray,
        uint handleCount,
        void* startTime,
        void* endTime);

    [LibraryImport("advapi32.dll", EntryPoint = "CloseTrace")]
    public static partial uint CloseTrace(ulong traceHandle);
}
