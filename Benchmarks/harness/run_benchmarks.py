"""Xvirus SDK / CLI benchmark orchestrator.

  py -3 run_benchmarks.py fresh-update   # full database download from an empty folder (+ script model)
  py -3 run_benchmarks.py load           # first-run and steady-state engine load, per SDK
  py -3 run_benchmarks.py profiles       # per-file / folder / update benchmarks, per SDK x engine profile
  py -3 run_benchmarks.py cli-oneshot    # CLI one process per command
  py -3 run_benchmarks.py scaling        # multi-threaded folder throughput (C# SDK)
"""
import json
import os
import platform
import shutil
import subprocess
import sys
import time
from pathlib import Path

from common import (CORPUS, DB, PROFILES, PUB, RESULTS, SDKS, WORK, proc_metrics, write_settings)

RAW = RESULTS / "raw"
RAW.mkdir(parents=True, exist_ok=True)
MANIFEST = json.loads((CORPUS / "manifest.json").read_text())
FILES = CORPUS / "files"


def fwd(p) -> str:
    return str(p).replace("\\", "/")


def host_cmd(sdk: str, plan: Path, out: Path):
    d = SDKS[sdk]
    if sdk == "CSharpSDK":
        return [str(d / "CSharpHost.exe"), str(d), str(plan), str(out)]
    if sdk == "NativeSDK":
        return [str(d / "native_host.exe"), str(d), str(plan), str(out)]
    if sdk == "NodeSDK":
        return ["node", str(Path(__file__).parent / "node-host" / "node_host.mjs"), str(d), str(plan), str(out)]
    raise ValueError(sdk)


def run_host(sdk: str, plan_lines: list[str], tag: str) -> dict:
    plan = WORK / f"plan_{tag}.txt"
    out = RAW / f"{tag}.jsonl"
    plan.write_text("\n".join(plan_lines) + "\n")
    t0 = time.perf_counter()
    p = subprocess.Popen(host_cmd(sdk, plan, out), stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    so, se = p.communicate()
    wall = (time.perf_counter() - t0) * 1000
    ext = proc_metrics(p._handle)
    if p.returncode != 0:
        print(f"!! {tag} exit {p.returncode}: {se.decode(errors='ignore')[-2000:]}", file=sys.stderr)
    ops = [json.loads(l) for l in out.read_text().splitlines() if l.strip()] if out.exists() else []
    rec = {"sdk": sdk, "tag": tag, "process_wall_ms": wall, "process": ext, "ops": ops, "exit": p.returncode}
    (RAW / f"{tag}.json").write_text(json.dumps(rec, indent=1))
    return rec


# ------------------------------------------------------------------ CLI (interactive driver)
class CliSession:
    def __init__(self):
        exe = SDKS["CLI"] / "XvirusCLI.exe"
        self.t0 = time.perf_counter()
        self.p = subprocess.Popen([str(exe), "i"], stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                                  stderr=subprocess.DEVNULL, cwd=str(SDKS["CLI"]), text=True, bufsize=1)
        time.sleep(0.05)

    def metrics(self):
        return proc_metrics(self.p._handle)

    def cmd(self, line: str, multiline=False) -> tuple[float, str]:
        t0 = time.perf_counter()
        self.p.stdin.write(line + "\n")
        self.p.stdin.flush()
        if not multiline:
            out = self.p.stdout.readline().strip()
        else:
            buf = []
            while True:
                l = self.p.stdout.readline()
                buf.append(l)
                if l.strip() in ("]", "[]") or l == "":
                    break
            out = "".join(buf)
        return (time.perf_counter() - t0) * 1000, out

    def close(self):
        try:
            self.p.stdin.write("q\n")
            self.p.stdin.flush()
        except OSError:
            pass
        self.p.wait(30)


def run_cli(plan_lines: list[str], tag: str) -> dict:
    s = CliSession()
    ops = []
    # Wait until the runtime is up: 'v' prints the version without loading the engine.
    tv, _ = s.cmd("v")
    m = s.metrics()
    ops.append({"op": "baseline", "path": "", "times_ms": [tv], "cpu_ms": 0, **m, "result": "", "files": 0, "threads": 1})
    for line in plan_lines:
        parts = line.split(" ", 1)
        op = parts[0]
        c0 = s.metrics()["cpu_ms"]
        if op == "baseline":
            continue
        if op == "load":
            t, r = s.cmd("l")
            ops.append({"op": "load", "path": "", "times_ms": [t], "cpu_ms": s.metrics()["cpu_ms"] - c0, **_mem(s), "result": r, "files": 0, "threads": 1})
        elif op == "update":
            t, r = s.cmd("up")
            ops.append({"op": "update", "path": "", "times_ms": [t], "cpu_ms": s.metrics()["cpu_ms"] - c0, **_mem(s), "result": r, "files": 0, "threads": 1})
        elif op == "scan":
            warm, reps, path = parts[1].split(" ", 2)
            warm_t, times, name = [], [], ""
            for _ in range(int(warm)):
                t, r = s.cmd("s " + path)
                warm_t.append(t)
            c1 = s.metrics()["cpu_ms"]
            for _ in range(int(reps)):
                t, r = s.cmd("s " + path)
                times.append(t)
            try:
                name = json.loads(r)["Name"]
            except Exception:
                name = r[:80]
            ops.append({"op": "scan", "path": path, "times_ms": times, "cpu_ms": s.metrics()["cpu_ms"] - c1, **_mem(s),
                        "result": f"{name}|warm=" + ",".join(f"{x:.3f}" for x in warm_t), "files": 0, "threads": 1})
        elif op == "folder":
            t, r = s.cmd("sf " + parts[1], multiline=True)
            try:
                results = json.loads(r)
                files, mal = len(results), sum(1 for x in results if x.get("IsMalware"))
            except Exception:
                files, mal = 0, 0
            ops.append({"op": "folder", "path": parts[1], "times_ms": [t], "cpu_ms": s.metrics()["cpu_ms"] - c0, **_mem(s),
                        "result": f"malware={mal}", "files": files, "threads": 1})
    s.close()
    rec = {"sdk": "CLI", "tag": tag, "process_wall_ms": (time.perf_counter() - s.t0) * 1000, "process": s.metrics(), "ops": ops, "exit": s.p.returncode}
    (RAW / f"{tag}.json").write_text(json.dumps(rec, indent=1))
    return rec


def _mem(s):
    m = s.metrics()
    return {"ws_mb": m["ws_mb"], "peak_ws_mb": m["peak_ws_mb"], "private_mb": m["private_mb"]}


def run_any(sdk, plan, tag):
    return run_cli(plan, tag) if sdk == "CLI" else run_host(sdk, plan, tag)


# ------------------------------------------------------------------ experiments
def fresh_update():
    # 1) Fresh install with every engine enabled (includes the ~940 MB script AI model).
    if DB.exists():
        shutil.rmtree(DB)
    DB.mkdir(parents=True)
    d = SDKS["CSharpSDK"]
    write_settings(d, "aiscript", DatabaseVersion={k: 0 for k in json.loads(json.dumps(write_settings(d)["DatabaseVersion"]))})
    rec = run_host("CSharpSDK", ["baseline", "update"], "fresh_update_full_aiscript")
    s = json.loads((d / "settings.json").read_text())
    (WORK / "db_versions.json").write_text(json.dumps(s["DatabaseVersion"]))
    print("full+script:", rec["ops"][-1]["result"], rec["ops"][-1]["times_ms"])

    # 2) Fresh install with the default engines (no script model), one per SDK binding.
    for sdk in ["NativeSDK", "NodeSDK"]:
        tmp = WORK / f"db_fresh_{sdk}"
        if tmp.exists():
            shutil.rmtree(tmp)
        tmp.mkdir()
        write_settings(SDKS[sdk], "full", DatabaseFolder=str(tmp),
                       DatabaseVersion={k: 0 for k in s["DatabaseVersion"]})
        rec = run_host(sdk, ["baseline", "update"], f"fresh_update_default_{sdk}")
        print(sdk, "default:", rec["ops"][-1]["result"], rec["ops"][-1]["times_ms"])
        shutil.rmtree(tmp, ignore_errors=True)


def load_bench():
    for sdk in SDKS:
        for i in range(3):
            write_settings(SDKS[sdk], "full")
            (SDKS[sdk] / "rules.json").unlink(missing_ok=True)
            run_any(sdk, ["baseline", "load"], f"load_first_{sdk}_{i}")
        for i in range(5):
            write_settings(SDKS[sdk], "full")
            run_any(sdk, ["baseline", "load"], f"load_steady_{sdk}_{i}")
        print("load", sdk, "done")


def scan_lines(profile: str) -> list[str]:
    lines = []
    for f in MANIFEST["files"]:
        if f["kind"] == "script":
            continue
        reps = 7 if f["bytes"] <= 2 * 1048576 else 5 if f["bytes"] <= 8 * 1048576 else 3
        if profile == "aiscript" and f["label"] not in ("2MB", "signed") and f["kind"] != "pe":
            continue
        if profile == "aiscript" and f["kind"] == "pe" and f["label"] != "2MB":
            continue
        lines.append(f"scan 1 {reps} {fwd(FILES / f['file'])}")
    for f in MANIFEST["files"]:
        if f["kind"] == "script":
            lines.append(f"scan 1 {3 if profile == 'aiscript' else 7} {fwd((FILES / f['file']).resolve())}")
    return lines


def prewarm_corpus():
    """Read every corpus file twice so OS cache and on-access AV scanning don't skew the first SDK run."""
    for _ in range(2):
        for f in CORPUS.rglob("*"):
            if f.is_file():
                f.read_bytes()


def profiles_bench(only_sdk=None, only_profile=None):
    prewarm_corpus()
    for profile in PROFILES:
        if only_profile and profile != only_profile:
            continue
        for sdk in SDKS:
            if only_sdk and sdk != only_sdk:
                continue
            write_settings(SDKS[sdk], profile)
            plan = ["baseline", "load"] + scan_lines(profile)
            if profile != "aiscript":
                plan += [f"folder {fwd(CORPUS / 'mixed')}"] * 2  # report the second (warm) pass
            if profile == "full":
                plan += ["update"] * 5
            t0 = time.time()
            rec = run_any(sdk, plan, f"profile_{profile}_{sdk}")
            print(f"{profile:9s} {sdk:10s} {time.time() - t0:6.1f}s exit={rec['exit']} ops={len(rec['ops'])}", flush=True)


def cli_oneshot():
    exe = SDKS["CLI"] / "XvirusCLI.exe"
    write_settings(SDKS["CLI"], "full")
    cases = [("version", ["v"]), ("load", ["l"]),
             ("scan_pe_512KB", ["s", fwd(FILES / "pe_512KB.dll")]),
             ("scan_pe_2MB", ["s", fwd(FILES / "pe_2MB.dll")]),
             ("scan_text_64KB", ["s", fwd(FILES / "text_64KB.log")]),
             ("scan_signed_pe", ["s", fwd(FILES / "signed_pe.exe")]),
             ("update", ["up"])]
    out = []
    for name, args in cases:
        reps = 5
        for i in range(reps):
            t0 = time.perf_counter()
            p = subprocess.Popen([str(exe)] + args, stdout=subprocess.PIPE, stderr=subprocess.PIPE, cwd=str(SDKS["CLI"]))
            so, _ = p.communicate()
            wall = (time.perf_counter() - t0) * 1000
            m = proc_metrics(p._handle)
            out.append({"case": name, "rep": i, "wall_ms": wall, **m, "stdout": so.decode(errors="ignore")[:200]})
        print("cli oneshot", name, "done", flush=True)
    (RAW / "cli_oneshot.json").write_text(json.dumps(out, indent=1))


def scaling():
    write_settings(SDKS["CSharpSDK"], "full")
    mixed = fwd(CORPUS / "mixed")
    plan = ["baseline", "load", f"pfolder 1 {mixed}"]  # warm-up pass
    for t in [1, 2, 4, 6, 8, 12]:
        plan.append(f"pfolder {t} {mixed}")
    run_host("CSharpSDK", plan, "scaling_CSharpSDK")
    print("scaling done")


def environment():
    import ctypes
    info = {
        "date": time.strftime("%Y-%m-%d"),
        "os": platform.platform(),
        "python": platform.python_version(),
        "cpu": subprocess.run(["powershell", "-NoProfile", "-c", "(Get-CimInstance Win32_Processor).Name"], capture_output=True, text=True).stdout.strip(),
        "cores_logical": os.cpu_count(),
        "ram_gb": round(int(subprocess.run(["powershell", "-NoProfile", "-c", "(Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory"], capture_output=True, text=True).stdout.strip()) / 1024**3, 1),
        "dotnet": subprocess.run(["dotnet", "--version"], capture_output=True, text=True).stdout.strip(),
        "node": subprocess.run(["node", "--version"], capture_output=True, text=True).stdout.strip(),
        "db_versions": json.loads((WORK / "db_versions.json").read_text()) if (WORK / "db_versions.json").exists() else None,
        "db_files": {p.name: p.stat().st_size for p in DB.iterdir()} if DB.exists() else None,
        "binaries": {sdk: sum(f.stat().st_size for f in d.rglob("*") if f.is_file() and f.suffix not in (".pdb", ".lib", ".xml") and "settings" not in f.name and "rules" not in f.name and "host" not in f.name.lower())
                     for sdk, d in SDKS.items()},
    }
    (RESULTS / "environment.json").write_text(json.dumps(info, indent=2))
    print(json.dumps(info, indent=2))


if __name__ == "__main__":
    cmd = sys.argv[1]
    {"fresh-update": fresh_update, "load": load_bench, "cli-oneshot": cli_oneshot, "scaling": scaling,
     "env": environment,
     "profiles": lambda: profiles_bench(*(sys.argv[2:4] + [None, None])[:2])}[cmd]()
