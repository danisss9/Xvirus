using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Xvirus;

namespace XvirusService.Services;

[SupportedOSPlatform("windows")]
public class RealTimeProtection(
    SettingsService settings,
    Scanner scanner,
    Quarantine quarantine,
    ServerEventService events,
    ThreatAlertService alertService) : IDisposable
{
    // Singleton ref for the unmanaged callback — one RTP instance per process
    private static RealTimeProtection? _current;

    private const string SessionName = "XvirusRTP";

    // Microsoft-Windows-Kernel-Process provider — process start/stop events
    private static readonly Guid KernelProcessProvider = new("22fb2cd6-0e7b-422b-a0c7-2fad1fd0e716");
    private const ulong ProcessKeyword = 0x10;  // WINEVENT_KEYWORD_PROCESS
    private const byte LevelInformation = 4;

    // Paths already scanned this session — avoids re-scanning the same executable
    private readonly HashSet<string> _scannedPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _scannedLock = new();

    private ulong _sessionHandle;
    private long _traceHandle = Etw.InvalidHandle;
    private Thread? _traceThread;
    private readonly object _lock = new();
    private bool _disposed;

    // -----------------------------------------------------------------------

    public void Start()
    {
        if (!settings.AppSettings.RealTimeProtection)
        {
            Console.WriteLine("RealTimeProtection: disabled, not starting.");
            return;
        }

        Console.WriteLine("RealTimeProtection: starting ETW process monitor...");
        try
        {
            _current = this;

            // seed the scanned-path set with executables for processes
            // already running when RTP starts.  ETW only gives us future
            // start events, so without this an existing process would be
            // scanned later if it restarted using the same path.  the set
            // is only used as a de‑duplication cache, we don't perform the
            // initial scan here.
            SeedScannedPaths();

            StartEtw();
            Console.WriteLine("RealTimeProtection: process monitor active.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"RealTimeProtection: failed to start – {ex.Message}");
        }
    }

    public void Stop()
    {
        StopEtw();
        Console.WriteLine("RealTimeProtection: stopped.");
    }

    // -----------------------------------------------------------------------
    // ETW session lifecycle
    // -----------------------------------------------------------------------

    /// <summary>
    /// Add paths of any processes already running to the scan cache.  The
    /// caller holds <see cref="_current"/> but the set is guarded by
    /// <see cref="_scannedLock"/> so this method can be called from any
    /// thread (we call it synchronously from <see cref="Start"/>).
    /// </summary>
    private void SeedScannedPaths()
    {
        try
        {
            foreach (var proc in Process.GetProcesses())
            {
                try
                {
                    string? exe = ProcessControl.ResolveProcessPath(proc.Id);
                    if (!string.IsNullOrEmpty(exe))
                    {
                        lock (_scannedLock)
                        {
                            _scannedPaths.Add(exe);
                        }
                    }
                }
                catch
                {
                    // ignore individual failures; some processes may vanish
                    // or refuse access by the time we inspect them.
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"RealTimeProtection: failed to enumerate processes – {ex.Message}");
        }
    }

    // -----------------------------------------------------------------------

    private unsafe void StartEtw()
    {
        int nameBytes = (SessionName.Length + 1) * 2; // null-terminated UTF-16
        int totalSize = sizeof(Etw.EVENT_TRACE_PROPERTIES) + nameBytes;

        IntPtr buf = Marshal.AllocHGlobal(totalSize);
        try
        {
            new Span<byte>((void*)buf, totalSize).Clear();
            var p = (Etw.EVENT_TRACE_PROPERTIES*)buf;
            p->Wnode.BufferSize = (uint)totalSize;
            p->Wnode.Flags = Etw.WNODE_FLAG_TRACED_GUID;
            p->Wnode.ClientContext = 1; // QPC clock
            p->LogFileMode = Etw.EVENT_TRACE_REAL_TIME_MODE;
            p->LoggerNameOffset = (uint)sizeof(Etw.EVENT_TRACE_PROPERTIES);

            ulong handle = 0;
            uint err = Etw.StartTraceW(&handle, SessionName, p);

            if (err == Etw.ERROR_ALREADY_EXISTS)
            {
                // Reclaim stale session from a previous (crashed) run
                Etw.ControlTraceW(0, SessionName, p, Etw.EVENT_TRACE_CONTROL_STOP);
                new Span<byte>((void*)buf, totalSize).Clear();
                p->Wnode.BufferSize = (uint)totalSize;
                p->Wnode.Flags = Etw.WNODE_FLAG_TRACED_GUID;
                p->Wnode.ClientContext = 1;
                p->LogFileMode = Etw.EVENT_TRACE_REAL_TIME_MODE;
                p->LoggerNameOffset = (uint)sizeof(Etw.EVENT_TRACE_PROPERTIES);
                err = Etw.StartTraceW(&handle, SessionName, p);
            }

            if (err != Etw.ERROR_SUCCESS)
                throw new InvalidOperationException($"StartTraceW failed: {err}");

            _sessionHandle = handle;
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }

        // Enable Microsoft-Windows-Kernel-Process provider
        Guid g = KernelProcessProvider;
        uint hr = Etw.EnableTraceEx2(
            _sessionHandle, &g,
            Etw.EVENT_CONTROL_CODE_ENABLE_PROVIDER,
            LevelInformation, ProcessKeyword, 0, 0, null);

        if (hr != Etw.ERROR_SUCCESS)
            throw new InvalidOperationException($"EnableTraceEx2 failed: {hr}");

        // Open real-time consumer and register EVENT_RECORD callback
        var logFile = new Etw.EVENT_TRACE_LOGFILEW();
        logFile.ProcessTraceMode = Etw.PROCESS_TRACE_MODE_REAL_TIME | Etw.PROCESS_TRACE_MODE_EVENT_RECORD;
        logFile.EventRecordCallback = &OnEventRecord;

        long th;
        fixed (char* name = SessionName)
        {
            logFile.LoggerName = name;
            th = Etw.OpenTraceW(&logFile);
        }

        if (th == Etw.InvalidHandle)
            throw new InvalidOperationException($"OpenTraceW failed: {Marshal.GetLastWin32Error()}");

        lock (_lock) { _traceHandle = th; }

        // ProcessTrace blocks until CloseTrace is called; run on a dedicated thread
        _traceThread = new Thread(static () =>
        {
            long h = _current!._traceHandle;
            Etw.ProcessTrace(&h, 1, null, null);
        })
        { IsBackground = true, Name = "RTP-ETW" };
        _traceThread.Start();
    }

    private unsafe void StopEtw()
    {
        long th;
        lock (_lock)
        {
            th = _traceHandle;
            _traceHandle = Etw.InvalidHandle;
        }

        // CloseTrace unblocks ProcessTrace on the consumer thread
        if (th != Etw.InvalidHandle)
            Etw.CloseTrace((ulong)th);

        // Stop the ETW session
        if (_sessionHandle != 0)
        {
            int nameBytes = (SessionName.Length + 1) * 2;
            int totalSize = sizeof(Etw.EVENT_TRACE_PROPERTIES) + nameBytes;
            IntPtr buf = Marshal.AllocHGlobal(totalSize);
            new Span<byte>((void*)buf, totalSize).Clear();
            var p = (Etw.EVENT_TRACE_PROPERTIES*)buf;
            p->Wnode.BufferSize = (uint)totalSize;
            p->Wnode.Flags = Etw.WNODE_FLAG_TRACED_GUID;
            Etw.ControlTraceW(_sessionHandle, null, p, Etw.EVENT_TRACE_CONTROL_STOP);
            Marshal.FreeHGlobal(buf);
            _sessionHandle = 0;
        }

        _traceThread?.Join(2000);
    }

    // -----------------------------------------------------------------------
    // ETW event callback — runs on the dedicated RTP-ETW thread
    // -----------------------------------------------------------------------

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static unsafe void OnEventRecord(Etw.EVENT_RECORD* record)
    {
        try
        {
            // EventID 1 = ProcessStart in Microsoft-Windows-Kernel-Process
            if (record->EventDescriptorId != 1) return;
            if (record->UserDataLength < 4) return;

            // UserData layout: ProcessID (uint32), ParentProcessID (uint32), ImageName...
            uint pid = *(uint*)record->UserData;
            if (pid == 0) return;

            Task.Run(() => _current?.HandleProcessAsync((int)pid));
        }
        catch { /* must not throw from an unmanaged callback */ }
    }

    // -----------------------------------------------------------------------

    private async Task HandleProcessAsync(int pid)
    {
        try
        {
            string? executablePath = ProcessControl.ResolveProcessPath(pid);
            if (string.IsNullOrEmpty(executablePath))
                return;

            lock (_scannedLock)
            {
                if (!_scannedPaths.Add(executablePath)) return;
            }

            var result = await Task.Run(() => scanner.ScanFile(executablePath));
            if (!result.IsMalware)
                return;

            Console.WriteLine($"RealTimeProtection: threat detected – '{executablePath}' (score {result.MalwareScore:F2})");

            await ProcessControl.HandleThreatAsync(
                quarantine, settings.AppSettings, events, alertService,
                result, pid, executablePath, string.Empty,
                "RealTimeProtection");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"RealTimeProtection: error processing pid {pid} – {ex.Message}");
        }
    }

    // -----------------------------------------------------------------------

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopEtw();
        if (ReferenceEquals(_current, this)) _current = null;
        GC.SuppressFinalize(this);
    }
}

// ---------------------------------------------------------------------------
// Minimal ETW P/Invoke layer — no external packages, Native AOT compatible
// ---------------------------------------------------------------------------

internal static unsafe partial class Etw
{
    internal const long InvalidHandle = unchecked((long)0xFFFFFFFFFFFFFFFF);
    internal const uint ERROR_SUCCESS = 0;
    internal const uint ERROR_ALREADY_EXISTS = 183;
    internal const uint WNODE_FLAG_TRACED_GUID = 0x00020000;
    internal const uint EVENT_TRACE_REAL_TIME_MODE = 0x00000100;
    internal const uint EVENT_TRACE_CONTROL_STOP = 1;
    internal const uint EVENT_CONTROL_CODE_ENABLE_PROVIDER = 1;
    internal const uint PROCESS_TRACE_MODE_REAL_TIME = 0x00000100;
    internal const uint PROCESS_TRACE_MODE_EVENT_RECORD = 0x10000000;

    // -------------------------------------------------------------------
    // Structures
    // -------------------------------------------------------------------

    /// <summary>WNODE_HEADER (48 bytes on x64)</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct WNODE_HEADER
    {
        public uint BufferSize;         // 0
        public uint ProviderId;         // 4
        public ulong HistoricalContext;  // 8  (union: Version+Linkage)
        public long TimeStamp;          // 16 (union: CountLost/KernelHandle/TimeStamp)
        public Guid Guid;              // 24
        public uint ClientContext;      // 40
        public uint Flags;             // 44
    }

    /// <summary>EVENT_TRACE_PROPERTIES (120 bytes on x64)</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct EVENT_TRACE_PROPERTIES
    {
        public WNODE_HEADER Wnode;             // 0
        public uint BufferSize;               // 48
        public uint MinimumBuffers;           // 52
        public uint MaximumBuffers;           // 56
        public uint MaximumFileSize;          // 60
        public uint LogFileMode;              // 64
        public uint FlushTimer;               // 68
        public uint EnableFlags;              // 72
        public int AgeLimit;                 // 76
        public uint NumberOfBuffers;          // 80
        public uint FreeBuffers;              // 84
        public uint EventsLost;               // 88
        public uint BuffersWritten;           // 92
        public uint LogBuffersLost;           // 96
        public uint RealTimeBuffersLost;      // 100
        public IntPtr LoggerThreadId;          // 104
        public uint LogFileNameOffset;        // 112
        public uint LoggerNameOffset;         // 116
    }

    /// <summary>
    /// EVENT_TRACE_LOGFILEW — explicit offsets for fields we use.
    /// Full size on x64: 448 bytes. The EventRecordCallback union is at offset 424.
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 448)]
    internal struct EVENT_TRACE_LOGFILEW
    {
        [FieldOffset(0)] public char* LogFileName;
        [FieldOffset(8)] public char* LoggerName;
        [FieldOffset(28)] public uint ProcessTraceMode;      // union with LogFileMode
        // Offset 32:  CurrentEvent (EVENT_TRACE, 88 bytes)
        // Offset 120: LogfileHeader (TRACE_LOGFILE_HEADER, 280 bytes)
        // Offset 400: BufferCallback (8 bytes)
        // Offset 408: BufferSize/Filled/EventsLost (3 × 4 bytes = 12 bytes)
        // Offset 420: 4 bytes padding (align next pointer to 8 bytes)
        [FieldOffset(424)] public delegate* unmanaged[Stdcall]<EVENT_RECORD*, void> EventRecordCallback;
    }

    /// <summary>
    /// EVENT_RECORD — explicit offsets for the fields we read.
    /// EventDescriptorId at 40, UserDataLength at 86, UserData at 96.
    /// </summary>
    [StructLayout(LayoutKind.Explicit)]
    internal struct EVENT_RECORD
    {
        // EventHeader.EventDescriptor.Id
        // EVENT_HEADER(80) starts at 0; EventDescriptor at offset 40 inside it; Id is first field.
        [FieldOffset(40)] public ushort EventDescriptorId;

        // ETW_BUFFER_CONTEXT (4) at 80, ExtendedDataCount (2) at 84, UserDataLength (2) at 86
        [FieldOffset(86)] public ushort UserDataLength;

        // ExtendedData ptr (8) at 88, UserData ptr (8) at 96
        [FieldOffset(96)] public void* UserData;
    }

    // -------------------------------------------------------------------
    // P/Invoke — all ETW APIs live in advapi32.dll (forwarded to sechost.dll on Win8+)
    // LibraryImport generates marshalling code at compile time for Native AOT.
    // -------------------------------------------------------------------

    [LibraryImport("advapi32.dll", EntryPoint = "StartTraceW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial uint StartTraceW(
        ulong* traceHandle,
        string instanceName,
        EVENT_TRACE_PROPERTIES* properties);

    [LibraryImport("advapi32.dll", EntryPoint = "ControlTraceW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial uint ControlTraceW(
        ulong traceHandle,
        [MarshalAs(UnmanagedType.LPWStr)] string? instanceName,
        EVENT_TRACE_PROPERTIES* properties,
        uint controlCode);

    [LibraryImport("advapi32.dll", EntryPoint = "EnableTraceEx2")]
    internal static partial uint EnableTraceEx2(
        ulong traceHandle,
        Guid* providerId,
        uint controlCode,
        byte level,
        ulong matchAnyKeyword,
        ulong matchAllKeyword,
        uint timeout,
        void* enableParameters);   // PENABLE_TRACE_PARAMETERS — pass null for defaults

    [LibraryImport("advapi32.dll", EntryPoint = "OpenTraceW")]
    internal static partial long OpenTraceW(EVENT_TRACE_LOGFILEW* logfile);

    [LibraryImport("advapi32.dll", EntryPoint = "ProcessTrace")]
    internal static partial uint ProcessTrace(
        long* handleArray,
        uint handleCount,
        void* startTime,    // null = process from now
        void* endTime);     // null = run until CloseTrace

    [LibraryImport("advapi32.dll", EntryPoint = "CloseTrace")]
    internal static partial uint CloseTrace(ulong traceHandle);
}
