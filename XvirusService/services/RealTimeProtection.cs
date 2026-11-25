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

    private static string SessionName => $"{AppInfo.EtwSessionPrefix}RTP";

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
    private long _traceHandle = EtwNative.InvalidHandle;
    private Thread? _traceThread;
    private RansomwareFileWatcher? _fileWatcher;
    private readonly object _lock = new();
    private volatile bool _running;
    private bool _disposed;

    public bool IsRunning => _running;

    // -----------------------------------------------------------------------

    public void Start()
    {
        if (_running) return;

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
            _running = true;
            Console.WriteLine("RealTimeProtection: process monitor active.");

            // Behavior protection is an Anti-Malware feature; keep disabled in Firewall mode.
            if (!settings.IsFirewall && settings.AppSettings.BehaviorProtection)
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
        if (!_running && _fileWatcher == null) return;
        _running = false;
        _fileWatcher?.Dispose();
        _fileWatcher = null;
        StopEtw();
        Console.WriteLine("RealTimeProtection: stopped.");
    }

    /// <summary>Reconciles running state and behavior watcher after settings change.</summary>
    public void ApplySettings()
    {
        if (settings.AppSettings.RealTimeProtection)
        {
            if (!_running)
            {
                Start();
                return;
            }

            bool wantBehavior = !settings.IsFirewall && settings.AppSettings.BehaviorProtection;
            if (wantBehavior && _fileWatcher == null)
            {
                _fileWatcher = new RansomwareFileWatcher(events, alertService, settings.AppSettings);
                _fileWatcher.Start();
            }
            else if (!wantBehavior && _fileWatcher != null)
            {
                _fileWatcher.Dispose();
                _fileWatcher = null;
            }
        }
        else
        {
            Stop();
        }
    }

    // -----------------------------------------------------------------------
    // ETW session lifecycle
    // -----------------------------------------------------------------------

    private unsafe void StartEtw()
    {
        ulong handle = EtwNative.StartRealTimeSession(SessionName, out uint err);
        if (handle == 0)
            throw new InvalidOperationException($"StartTraceW failed: {err}");

        _sessionHandle = handle;

        // Enable Microsoft-Windows-Kernel-Process provider
        Guid g = KernelProcessProvider;
        uint hr = EtwNative.EnableTraceEx2(
            _sessionHandle, &g,
            EtwNative.EVENT_CONTROL_CODE_ENABLE_PROVIDER,
            LevelInformation, ProcessKeyword, 0, 0, null);

        if (hr != EtwNative.ERROR_SUCCESS)
            throw new InvalidOperationException($"EnableTraceEx2 failed: {hr}");

        // Open real-time consumer and register EVENT_RECORD callback
        var logFile = new EtwNative.EVENT_TRACE_LOGFILEW();
        logFile.ProcessTraceMode = EtwNative.PROCESS_TRACE_MODE_REAL_TIME | EtwNative.PROCESS_TRACE_MODE_EVENT_RECORD;
        logFile.EventRecordCallback = &OnEventRecord;

        long th;
        fixed (char* name = SessionName)
        {
            logFile.LoggerName = name;
            th = EtwNative.OpenTraceW(&logFile);
        }

        if (th == EtwNative.InvalidHandle)
            throw new InvalidOperationException($"OpenTraceW failed: {Marshal.GetLastWin32Error()}");

        lock (_lock) { _traceHandle = th; }

        // ProcessTrace blocks until CloseTrace is called; run on a dedicated thread
        _traceThread = new Thread(static () =>
        {
            long h = _current!._traceHandle;
            EtwNative.ProcessTrace(&h, 1, null, null);
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
            _traceHandle = EtwNative.InvalidHandle;
        }

        // CloseTrace unblocks ProcessTrace on the consumer thread
        if (th != EtwNative.InvalidHandle)
            EtwNative.CloseTrace((ulong)th);

        // Stop the ETW session
        if (_sessionHandle != 0)
        {
            EtwNative.StopSession(_sessionHandle, SessionName);
            _sessionHandle = 0;
        }

        _traceThread?.Join(2000);
    }

    // -----------------------------------------------------------------------
    // ETW event callback — runs on the dedicated RTP-ETW thread
    // -----------------------------------------------------------------------

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static unsafe void OnEventRecord(EtwNative.EVENT_RECORD* record)
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

            // Behavior protection is Anti-Malware only (disabled in Firewall mode).
            if (!settings.IsFirewall && settings.AppSettings.BehaviorProtection)
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
