"""Render markdown tables from results/summary.json into results/tables.md."""
import json
from common import RESULTS

d = json.loads((RESULTS / "summary.json").read_text())
SDKS = ["CSharpSDK", "NativeSDK", "NodeSDK", "CLI"]
NAMES = {"CSharpSDK": "C# SDK", "NativeSDK": "Native SDK", "NodeSDK": "Node.js SDK", "CLI": "CLI"}
PROFILES = ["full", "heur", "ai", "sig"]
PNAMES = {"full": "Full (Sig+Heur+AI)", "heur": "Sig+Heuristics", "ai": "Sig+AI", "sig": "Signatures only",
          "aiscript": "Full + AI Script"}
out = []


def f(v, nd=1):
    if v is None:
        return "–"
    if abs(v) >= 1000:
        return f"{v:,.0f}"
    if abs(v) >= 100:
        return f"{v:.0f}"
    return f"{v:.{nd}f}"


def table(title, header, rows):
    out.append(f"### {title}\n")
    out.append("| " + " | ".join(header) + " |")
    out.append("|" + "|".join(["---"] + ["--:"] * (len(header) - 1)) + "|")
    for r in rows:
        out.append("| " + " | ".join(str(x) for x in r) + " |")
    out.append("")


def size(b):
    return f"{b/1048576:.0f} MB" if b >= 1048576 else f"{b/1024:.0f} KB"


# Load
for phase in ("first", "steady"):
    rows = []
    for s in SDKS:
        L = d["load"][s][phase]
        rows.append([NAMES[s], f(L["time_ms"]), f(L["cpu_ms"]), f(L["baseline_ws_mb"]), f(L["ws_mb"]),
                     f(L["engine_ws_mb"]), f(L["peak_ws_mb"]), f(L["process_peak_private_mb"])])
    table(f"load_{phase}", ["Product", "Load time (ms)", "CPU time (ms)", "Host baseline WS (MB)", "WS after load (MB)",
                            "Engine footprint (MB)", "Peak WS (MB)", "Peak private (MB)"], rows)

# Load by profile
rows = []
for p in PROFILES + ["aiscript"]:
    rows.append([PNAMES[p]] + [f(d["profiles"][p][s]["load_ms"]) for s in SDKS] +
                [f(d["profiles"][p]["CSharpSDK"]["engine_ws_mb"])])
table("load_by_profile", ["Engine profile"] + [NAMES[s] + " (ms)" for s in SDKS] + ["Engine footprint (MB)"], rows)

files = d["files"]
pe = [x for x in files if x["kind"] == "pe"]
text = [x for x in files if x["kind"] == "text"]
binf = [x for x in files if x["kind"] == "binary"]


def scan(p, s, fn):
    return d["profiles"][p][s]["scans"].get(fn)


# PE by SDK (full)
for kind, lst in (("pe", pe), ("text", text), ("binary", binf)):
    rows = []
    for x in lst:
        r = [size(x["bytes"])]
        for s in SDKS:
            sc = scan("full", s, x["file"])
            r.append(f(sc["median_ms"], 2) if sc else "–")
        sc = scan("full", "NativeSDK", x["file"])
        r.append(f(sc["mb_s"]) if sc else "–")
        r.append(f(scan("full", "CSharpSDK", x["file"])["cpu_ms_per_scan"]))
        rows.append(r)
    table(f"{kind}_by_sdk", ["File size"] + [NAMES[s] + " (ms)" for s in SDKS] + ["Native MB/s", "CPU ms / scan (C#)"], rows)

# by engine profile (Native SDK, the reference)
for kind, lst in (("pe", pe), ("text", text), ("binary", binf)):
    rows = []
    for x in lst:
        r = [size(x["bytes"])]
        for p in PROFILES:
            sc = scan(p, "NativeSDK", x["file"])
            r.append(f(sc["median_ms"], 2) if sc else "–")
        rows.append(r)
    table(f"{kind}_by_engine", ["File size"] + [PNAMES[p] + " (ms)" for p in PROFILES], rows)

# first-scan (cold) latency
rows = []
for fn in ("pe_2MB.dll", "text_64KB.log", "signed_pe.exe"):
    rows.append([fn] + [f(scan("full", s, fn)["first_ms"]) + " / " + f(scan("full", s, fn)["median_ms"], 2) for s in SDKS])
table("first_vs_warm", ["File"] + [NAMES[s] + " first / warm (ms)" for s in SDKS], rows)

# signed
rows = []
for s in SDKS:
    a = scan("full", s, "signed_pe.exe")
    b = scan("full", s, "pe_128KB.dll")
    rows.append([NAMES[s], f(a["median_ms"], 2), f(b["median_ms"], 2), a["verdict"]])
table("signed", ["Product", "Signed PE 163 KB (ms)", "Unsigned PE 128 KB (ms)", "Verdict"], rows)

# folder
for p in PROFILES:
    rows = []
    for s in SDKS:
        F = d["profiles"][p][s]["folder"]
        rows.append([NAMES[s], f(F["time_ms"]), f(F["files_s"]), f(F["mb_s"]), f(F["cpu_pct_core"]), f(F["ws_mb"]),
                     f(d["profiles"][p][s]["peak_ws_mb"])])
    table(f"folder_{p}", ["Product", "Time (ms)", "Files/s", "MB/s", "CPU (% of one core)", "WS after (MB)", "Peak WS (MB)"], rows)

# folder summary across profiles
rows = []
for p in PROFILES:
    rows.append([PNAMES[p]] + [f(d["profiles"][p][s]["folder"]["files_s"]) for s in SDKS] +
                [f(d["profiles"][p]["NativeSDK"]["folder"]["cpu_pct_core"])])
table("folder_summary", ["Engine profile"] + [NAMES[s] + " files/s" for s in SDKS] + ["CPU % core (Native)"], rows)

# update
rows = []
for s in SDKS:
    U = d["profiles"]["full"][s]["update"]
    rows.append([NAMES[s], f(U["first_ms"]), f(U["median_ms"]), f(U["cpu_ms"]), f(U["ws_delta_mb"], 2), U["result"]])
table("update", ["Product", "First check (ms)", "Median check (ms)", "CPU (ms)", "WS delta (MB)", "Result"], rows)
rows = []
for k, v in d["fresh_update"].items():
    rows.append([k, NAMES[v["sdk"]], f(v["time_ms"]), f(v["cpu_ms"]), f(v["peak_ws_mb"]), v["result"]])
table("fresh_update", ["Run", "Product", "Time (ms)", "CPU (ms)", "Peak WS (MB)", "Result"], rows)

# aiscript
rows = []
for fn in ("backup.ps1", "build.bat", "server.js", "report.py", "pe_2MB.dll", "text_2MB.log"):
    r = [fn]
    for s in SDKS:
        sc = scan("aiscript", s, fn)
        r.append(f(sc["median_ms"]) if sc else "did not complete")
    rows.append(r)
table("aiscript", ["File"] + [NAMES[s] + " (ms)" for s in SDKS], rows)
rows = []
for s in SDKS:
    P = d["profiles"]["aiscript"][s]
    rows.append([NAMES[s], f(P["load_ms"]), f(P["ws_after_load_mb"]), f(P["peak_ws_mb"]), f(P["peak_private_mb"])])
table("aiscript_mem", ["Product", "Load (ms)", "WS after load (MB)", "Peak WS (MB)", "Peak private (MB)"], rows)

# cli oneshot
rows = []
for k, v in d["cli_oneshot"].items():
    rows.append([k, f(v["wall_ms"]), f(v["cpu_ms"]), f(v["peak_ws_mb"]), f(v["peak_private_mb"])])
table("cli_oneshot", ["Command", "Wall time (ms)", "CPU (ms)", "Peak WS (MB)", "Peak private (MB)"], rows)

# scaling
rows = []
base = d["scaling"][0]["files_s"]
for x in d["scaling"]:
    rows.append([x["threads"], f(x["time_ms"]), f(x["files_s"]), f(x["mb_s"]), f(x["files_s"] / base, 2) + "×",
                 f(x["cpu_pct_core"]), f(x["ws_mb"])])
table("scaling", ["Threads", "Time (ms)", "Files/s", "MB/s", "Speed-up", "CPU (% of one core)", "WS (MB)"], rows)

# peak memory per profile
rows = []
for p in PROFILES + ["aiscript"]:
    rows.append([PNAMES[p]] + [f(d["profiles"][p][s]["peak_ws_mb"]) for s in SDKS])
table("peak_by_profile", ["Engine profile"] + [NAMES[s] + " peak WS (MB)" for s in SDKS], rows)

(RESULTS / "tables.md").write_text("\n".join(out), encoding="utf-8")
print("\n".join(out))
