using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;
using Xvirus;
using Xvirus.Model;
using XvirusService.Model;

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

    // Behavior protection — classic macro-malware chain: an Office app spawning a
    // script host / shell. Low false-positive set that runs only when enabled.
    private static readonly HashSet<string> SuspiciousParents = new(StringComparer.OrdinalIgnoreCase)
        { "winword", "excel", "powerpnt", "outlook", "msaccess" };
    private static readonly HashSet<string> SuspiciousChildren = new(StringComparer.OrdinalIgnoreCase)
        { "powershell", "pwsh", "cmd", "wscript", "cscript", "mshta" };

    // LOLBins — legitimate signed binaries abused for execution / download /
    // decode. Flagged when launched by an Office app (macro chain) OR when the
    // command line matches a known-abuse pattern (see <see cref="IsLolbinAbuse"/>).
    private static readonly HashSet<string> Lolbins = new(StringComparer.OrdinalIgnoreCase)
        { "rundll32", "regsvr32", "mshta", "certutil" };

    // Ransomware tooling — command-line utilities abused to delete shadow copies
    // and disable recovery. Matched by command-line content (not merely by name)
    // so legitimate admin use of vssadmin/wbadmin/bcdedit does not raise an alert.
    private static readonly Dictionary<string, Func<string, bool>> RansomwareTooling = new(StringComparer.OrdinalIgnoreCase)
    {
        ["vssadmin"] = cmd => cmd.Contains("delete shadows", StringComparison.OrdinalIgnoreCase),
        ["wbadmin"] = cmd => cmd.Contains("delete catalog", StringComparison.OrdinalIgnoreCase)
                             || cmd.Contains("delete systemstatebackup", StringComparison.OrdinalIgnoreCase),
        ["bcdedit"] = cmd => cmd.Contains("ignoreallfailures", StringComparison.OrdinalIgnoreCase)
                             || (cmd.Contains("recoveryenabled", StringComparison.OrdinalIgnoreCase)
                                 && cmd.Contains("no", StringComparison.OrdinalIgnoreCase)),
        // Script hosts / shells invoking the tooling above are treated as
        // ransomware-tooling too — catches `powershell -c "vssadmin ..."` etc.
        ["powershell"] = IsRansomwareToolingInvocation,
        ["pwsh"] = IsRansomwareToolingInvocation,
        ["cmd"] = IsRansomwareToolingInvocation,
        ["wscript"] = IsRansomwareToolingInvocation,
        ["cscript"] = IsRansomwareToolingInvocation,
    };

    private static bool IsRansomwareToolingInvocation(string cmd) =>
        cmd.Contains("vssadmin", StringComparison.OrdinalIgnoreCase)
        || cmd.Contains("wbadmin", StringComparison.OrdinalIgnoreCase)
        || cmd.Contains("bcdedit", StringComparison.OrdinalIgnoreCase)
        || cmd.Contains("DeleteShadowCopies", StringComparison.OrdinalIgnoreCase)
        || cmd.Contains("Win32_ShadowCopy", StringComparison.OrdinalIgnoreCase);

    // Known ransomware-style extensions appended to encrypted files. Used by the
    // mass-rename/encrypt watcher to weight extension-changing renames.
    private static readonly HashSet<string> RansomExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".encrypted", ".locked", ".crypted", ".crypt", ".locky", ".cerber",
          ".crypto", ".enc", ".ransom", ".payday", ".gandcrab", ".sodinokibi" };

    private ulong _sessionHandle;
    private long _traceHandle = Etw.InvalidHandle;
    private Thread? _traceThread;
    private RansomwareFileWatcher? _fileWatcher;
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
            StartEtw();
            Console.WriteLine("RealTimeProtection: process monitor active.");

            if (settings.AppSettings.BehaviorProtection)
            {
                _fileWatcher = new RansomwareFileWatcher(events, alertService, settings.AppSettings);
                _fileWatcher.Start();
                Console.WriteLine("RealTimeProtection: ransomware mass-rename/encrypt watcher active.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"RealTimeProtection: failed to start – {ex.Message}");
        }
    }

    public void Stop()
    {
        _fileWatcher?.Dispose();
        _fileWatcher = null;
        StopEtw();
        Console.WriteLine("RealTimeProtection: stopped.");
    }

    // -----------------------------------------------------------------------
    // ETW session lifecycle
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

            int parentPid = record->UserDataLength >= 8
                ? (int)*(uint*)((byte*)record->UserData + 4)
                : 0;

            Task.Run(() => _current?.HandleProcessAsync((int)pid, parentPid));
        }
        catch { /* must not throw from an unmanaged callback */ }
    }

    // -----------------------------------------------------------------------

    private async Task HandleProcessAsync(int pid, int parentPid = 0)
    {
        try
        {
            string? executablePath = ProcessControl.ResolveProcessPath(pid);
            if (string.IsNullOrEmpty(executablePath))
                return;

            // Behavior protection runs per-launch (before the per-path scan dedup) so a
            // legitimate-when-run-once binary is still caught in a suspicious chain.
            if (settings.AppSettings.BehaviorProtection)
            {
                var hit = EvaluateBehavior(parentPid, pid, executablePath);
                if (hit is { } behaviorHit)
                {
                    Console.WriteLine(
                        $"RealTimeProtection: {behaviorHit.Kind} – '{executablePath}' from parent pid {parentPid}. cmd='{behaviorHit.CommandLine}'");
                    var behaviorResult = new ScanResult(1, behaviorHit.ThreatName, executablePath);
                    await ProcessControl.HandleThreatAsync(
                        quarantine, settings.AppSettings, events, alertService,
                        behaviorResult, pid, executablePath, string.Empty,
                        "BehaviorProtection");
                    return;
                }
            }

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

    /// <summary>Result of a behavior-protection match.</summary>
    private sealed record BehaviorHit(string Kind, string ThreatName, string CommandLine);

    /// <summary>
    /// Evaluates a newly-started process against the expanded behavior rules:
    /// ransomware tooling (vssadmin/wbadmin/bcdedit), LOLBins
    /// (rundll32/regsvr32/mshta/certutil), and the classic Office→script chain.
    /// Returns <c>null</c> when no rule matches.
    /// </summary>
    private static BehaviorHit? EvaluateBehavior(int parentPid, int pid, string executablePath)
    {
        var name = Path.GetFileNameWithoutExtension(executablePath);
        string? cmd = ProcessControl.GetCommandLine(pid);

        // 1. Ransomware tooling — matched by command-line content regardless of
        //    parent (these are rarely invoked by Office directly; the abuse is in
        //    the arguments). Legitimate admin use without the malicious args is
        //    not flagged.
        if (cmd is not null
            && RansomwareTooling.TryGetValue(name, out var matcher)
            && matcher(cmd))
        {
            return new BehaviorHit("ransomware-tooling", "Behavior.RansomwareTooling", cmd);
        }

        // 2. LOLBins — abuse by command line (URL fetch / encoded payload / script
        //    URL) OR launched by an Office app (macro-driven LOLBin execution).
        if (Lolbins.Contains(name))
        {
            if (IsLolbinAbuse(cmd))
                return new BehaviorHit("lolbin-abuse", "Behavior.LOLBinAbuse", cmd ?? string.Empty);
            if (IsOfficeParent(parentPid))
                return new BehaviorHit("lolbin-from-office", "Behavior.LOLBinFromOffice", cmd ?? string.Empty);
        }

        // 3. Classic Office→script host / shell chain (pre-existing rule).
        if (SuspiciousChildren.Contains(name) && IsOfficeParent(parentPid))
            return new BehaviorHit("office-script-chain", "Behavior.SuspiciousScriptHost", cmd ?? string.Empty);

        return null;
    }

    // True when the parent process is an Office application.
    private static bool IsOfficeParent(int parentPid)
    {
        if (parentPid <= 0) return false;
        try
        {
            using var parent = Process.GetProcessById(parentPid);
            return SuspiciousParents.Contains(parent.ProcessName);
        }
        catch
        {
            return false; // parent already exited or inaccessible
        }
    }

    // LOLBin command-line abuse indicators: remote URL fetch (regsvr32
    // Squiblydoo, mshta URL, rundll32 URL), inline script engines, and
    // certutil download/decode primitives.
    private static bool IsLolbinAbuse(string? commandLine)
    {
        if (string.IsNullOrEmpty(commandLine)) return false;
        return commandLine.Contains("http://", StringComparison.OrdinalIgnoreCase)
               || commandLine.Contains("https://", StringComparison.OrdinalIgnoreCase)
               || commandLine.Contains("javascript:", StringComparison.OrdinalIgnoreCase)
               || commandLine.Contains("vbscript:", StringComparison.OrdinalIgnoreCase)
               || commandLine.Contains("-decode", StringComparison.OrdinalIgnoreCase)
               || commandLine.Contains("-urlcache", StringComparison.OrdinalIgnoreCase)
               || commandLine.Contains("-ping", StringComparison.OrdinalIgnoreCase)
               || commandLine.Contains("/u /i:", StringComparison.OrdinalIgnoreCase)
               || commandLine.Contains("scrobj", StringComparison.OrdinalIgnoreCase);
    }

    // -----------------------------------------------------------------------

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _fileWatcher?.Dispose();
        _fileWatcher = null;
        StopEtw();
        if (ReferenceEquals(_current, this)) _current = null;
        GC.SuppressFinalize(this);
    }

    // -----------------------------------------------------------------------
    // Ransomware mass-rename / mass-encrypt heuristic
    // -----------------------------------------------------------------------

    /// <summary>
    /// Userland ransomware heuristic — watches common user document folders for
    /// a burst of rename / extension-change events characteristic of mass
    /// encryption. Without a minifilter we cannot attribute file ops to a PID,
    /// so the response is an alert (SSE + pending action) rather than a process
    /// kill. The burst thresholds are tuned well above normal user activity.
    /// </summary>
    private sealed class RansomwareFileWatcher : IDisposable
    {
        private const int BurstWindowSeconds = 10;
        private const int RenameBurstThreshold = 50;   // renames in window
        private const int EncryptBurstThreshold = 15;  // extension-changing renames in window
        private static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(30);

        private readonly ServerEventService _events;
        private readonly ThreatAlertService _alertService;
        private readonly AppSettingsDTO _appSettings;
        private readonly List<FileSystemWatcher> _watchers = new();
        private readonly Queue<DateTime> _renameTimes = new();
        private readonly Queue<DateTime> _encryptTimes = new();
        private readonly object _lock = new();
        private DateTime _lastAlert = DateTime.MinValue;
        private bool _disposed;

        public RansomwareFileWatcher(ServerEventService events, ThreatAlertService alertService, AppSettingsDTO appSettings)
        {
            _events = events;
            _alertService = alertService;
            _appSettings = appSettings;
        }

        public void Start()
        {
            foreach (var folder in EnumerateUserFolders())
            {
                try
                {
                    var w = new FileSystemWatcher(folder)
                    {
                        IncludeSubdirectories = true,
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
                        InternalBufferSize = 16 * 1024,
                    };
                    w.Renamed += OnRenamed;
                    w.Error += (_, e) =>
                        Console.WriteLine($"RansomwareFileWatcher: error on '{folder}' – {e.GetException().Message}");
                    w.EnableRaisingEvents = true;
                    _watchers.Add(w);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"RansomwareFileWatcher: could not watch '{folder}' – {ex.Message}");
                }
            }
        }

        private static IEnumerable<string> EnumerateUserFolders()
        {
            var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Current user profile (works whether the service runs as a user or SYSTEM)
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(profile)) roots.Add(profile);

            // All interactive user profiles under C:\Users (SYSTEM service case)
            try
            {
                if (Directory.Exists(@"C:\Users"))
                {
                    foreach (var d in Directory.EnumerateDirectories(@"C:\Users"))
                    {
                        string name = Path.GetFileName(d);
                        if (name.Equals("Public", StringComparison.OrdinalIgnoreCase)
                            || name.StartsWith(".", StringComparison.OrdinalIgnoreCase))
                            continue;
                        roots.Add(d);
                    }
                }
            }
            catch { /* access denied — fall back to current profile only */ }

            foreach (var root in roots)
            {
                foreach (var sub in new[] { "Desktop", "Documents", "Pictures", "Videos", "Music" })
                {
                    string p = Path.Combine(root, sub);
                    if (Directory.Exists(p)) yield return p;
                }
            }
        }

        private void OnRenamed(object sender, RenamedEventArgs e)
        {
            try
            {
                bool extensionChanged = false;
                string? oldExt = Path.GetExtension(e.OldName);
                string? newExt = Path.GetExtension(e.Name);
                if (!string.Equals(oldExt, newExt, StringComparison.OrdinalIgnoreCase))
                {
                    // Appended extension (e.g. report.docx.locky) or a known ransom ext
                    extensionChanged = !string.IsNullOrEmpty(newExt)
                        && (RansomExtensions.Contains(newExt)
                            || (newExt.Length > (oldExt?.Length ?? 0)
                                && oldExt is not null
                                && newExt.EndsWith(oldExt, StringComparison.OrdinalIgnoreCase)));
                }

                DateTime now = DateTime.UtcNow;
                string folder = ((FileSystemWatcher)sender).Path ?? string.Empty;

                lock (_lock)
                {
                    _renameTimes.Enqueue(now);
                    if (extensionChanged) _encryptTimes.Enqueue(now);
                    Prune(_renameTimes, now);
                    Prune(_encryptTimes, now);

                    if (now - _lastAlert < Cooldown) return;

                    int renameCount = _renameTimes.Count;
                    int encryptCount = _encryptTimes.Count;

                    string? threatName = null;
                    if (encryptCount >= EncryptBurstThreshold)
                        threatName = "Behavior.RansomwareMassEncrypt";
                    else if (renameCount >= RenameBurstThreshold)
                        threatName = "Behavior.RansomwareMassRename";

                    if (threatName is null) return;

                    _lastAlert = now;
                    _ = RaiseAlertAsync(threatName, folder, renameCount, encryptCount);
                }
            }
            catch { /* never throw from a FSW callback */ }
        }

        private static void Prune(Queue<DateTime> q, DateTime now)
        {
            var cutoff = now.AddSeconds(-BurstWindowSeconds);
            while (q.Count > 0 && q.Peek() < cutoff) q.Dequeue();
        }

        private async Task RaiseAlertAsync(string threatName, string folder, int renameCount, int encryptCount)
        {
            try
            {
                Console.WriteLine(
                    $"RansomwareFileWatcher: {threatName} on '{folder}' ({renameCount} renames, {encryptCount} extension changes in {BurstWindowSeconds}s).");

                var evt = new ThreatEventDTO
                {
                    FilePath = folder,
                    FileName = Path.GetFileName(folder.TrimEnd('\\')),
                    ProcessName = string.Empty,
                    ProcessId = 0,
                    ThreatName = threatName,
                    MalwareScore = 1.0,
                    Action = "suspended",
                    ShowNotification = _appSettings.ShowNotifications,
                    AlreadyQuarantined = false,
                };

                Logger.LogHistory("threat",
                    $"BehaviorProtection: {threatName} detected in '{folder}' – {renameCount} renames / {encryptCount} extension changes in {BurstWindowSeconds}s.");

                _alertService.AddPending(evt);
                await _events.SendAsync(
                    "threat",
                    JsonSerializer.Serialize(evt, AppJsonSerializerContext.Default.ThreatEventDTO));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"RansomwareFileWatcher: failed to raise alert – {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var w in _watchers)
            {
                try { w.EnableRaisingEvents = false; w.Dispose(); }
                catch { }
            }
            _watchers.Clear();
        }
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
