"""Shared paths, settings profiles and Windows process-metric helpers for the Xvirus benchmark."""
import ctypes
import ctypes.wintypes as wt
import json
import os
from pathlib import Path

HARNESS = Path(__file__).resolve().parent
BENCH = HARNESS.parent
WORK = Path(os.environ.get("XV_BENCH_WORK", HARNESS / "_work"))
PUB = WORK / "pub"
DB = WORK / "db"
CORPUS = WORK / "corpus"
RESULTS = BENCH / "results"

SDKS = {
    # name: (deploy folder, command builder)
    "CSharpSDK": PUB / "csharp",
    "NativeSDK": PUB / "native",
    "NodeSDK": PUB / "node",
    "CLI": PUB / "cli",
}

BASE_SETTINGS = {
    "EnableSignatures": True,
    "EnableHeuristics": True,
    "EnableAIScan": True,
    "EnableAIScriptScan": False,
    "EnableCloudScan": False,
    "OnlyScanExecutables": False,
    "HeuristicsLevel": 4,
    "AILevel": 10,
    "MaxScanLength": None,
    "MaxHeuristicsPeScanLength": 20971520,
    "MaxHeuristicsOthersScanLength": 10485760,
    "MaxAIScanLength": 20971520,
    "MaxAIScriptScanLength": 1048576,
    "EnableArchiveScan": False,
    "MaxArchiveDepth": None,
    "MaxArchiveTotalSize": None,
    "MaxArchiveFileCount": None,
    "CheckSDKUpdates": True,
    "DatabaseFolder": str(DB),
    "LastUpdateCheck": None,
    "DatabaseVersion": {
        "AIModel": 0, "ScriptAIModel": 0, "MainDB": 0, "DailyDB": 0, "WhiteDB": 0,
        "DailywlDB": 0, "HeurDB": 0, "HeurDB2": 0, "MalvendorDB": 0,
    },
}

# Engine profiles benchmarked. Everything not listed keeps the SDK default.
PROFILES = {
    "full":      {"label": "Signatures + Heuristics + AI (default)", "EnableHeuristics": True,  "EnableAIScan": True},
    "heur":      {"label": "Signatures + Heuristics",                "EnableHeuristics": True,  "EnableAIScan": False},
    "ai":        {"label": "Signatures + AI",                        "EnableHeuristics": False, "EnableAIScan": True},
    "sig":       {"label": "Signatures only",                        "EnableHeuristics": False, "EnableAIScan": False},
    "aiscript":  {"label": "Full + AI Script (LLM)",                 "EnableHeuristics": True,  "EnableAIScan": True, "EnableAIScriptScan": True},
}


def db_versions():
    p = WORK / "db_versions.json"
    return json.loads(p.read_text()) if p.exists() else BASE_SETTINGS["DatabaseVersion"]


def write_settings(folder: Path, profile: str | None = None, **overrides):
    s = json.loads(json.dumps(BASE_SETTINGS))
    s["DatabaseVersion"] = db_versions()
    if profile:
        for k, v in PROFILES[profile].items():
            if k != "label":
                s[k] = v
    s.update(overrides)
    (folder / "settings.json").write_text(json.dumps(s, indent=2))
    return s


# ---------------------------------------------------------------- Windows process metrics
class PROCESS_MEMORY_COUNTERS_EX(ctypes.Structure):
    _fields_ = [
        ("cb", wt.DWORD), ("PageFaultCount", wt.DWORD),
        ("PeakWorkingSetSize", ctypes.c_size_t), ("WorkingSetSize", ctypes.c_size_t),
        ("QuotaPeakPagedPoolUsage", ctypes.c_size_t), ("QuotaPagedPoolUsage", ctypes.c_size_t),
        ("QuotaPeakNonPagedPoolUsage", ctypes.c_size_t), ("QuotaNonPagedPoolUsage", ctypes.c_size_t),
        ("PagefileUsage", ctypes.c_size_t), ("PeakPagefileUsage", ctypes.c_size_t),
        ("PrivateUsage", ctypes.c_size_t),
    ]


_psapi = ctypes.WinDLL("psapi")
_k32 = ctypes.WinDLL("kernel32")
_psapi.GetProcessMemoryInfo.argtypes = [wt.HANDLE, ctypes.c_void_p, wt.DWORD]
_k32.GetProcessTimes.argtypes = [wt.HANDLE] + [ctypes.POINTER(wt.FILETIME)] * 4


def proc_metrics(handle) -> dict:
    """CPU time and memory counters for a process handle (works after the process has exited)."""
    pmc = PROCESS_MEMORY_COUNTERS_EX()
    pmc.cb = ctypes.sizeof(pmc)
    _psapi.GetProcessMemoryInfo(wt.HANDLE(int(handle)), ctypes.byref(pmc), pmc.cb)
    c, e, k, u = wt.FILETIME(), wt.FILETIME(), wt.FILETIME(), wt.FILETIME()
    _k32.GetProcessTimes(wt.HANDLE(int(handle)), ctypes.byref(c), ctypes.byref(e), ctypes.byref(k), ctypes.byref(u))
    ft = lambda f: ((f.dwHighDateTime << 32) | f.dwLowDateTime) / 10000.0
    return {
        "cpu_ms": ft(k) + ft(u),
        "ws_mb": pmc.WorkingSetSize / 1048576,
        "peak_ws_mb": pmc.PeakWorkingSetSize / 1048576,
        "private_mb": pmc.PrivateUsage / 1048576,
        "peak_private_mb": pmc.PeakPagefileUsage / 1048576,
    }
