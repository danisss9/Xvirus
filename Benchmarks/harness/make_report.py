"""Aggregates raw benchmark output into results/summary.json, results/tables.md and charts/*.svg."""
import json
import statistics as st
from pathlib import Path

from common import PROFILES, RESULTS
import svgcharts as sc

RAW = RESULTS / "raw"
CHARTS = RESULTS.parent / "charts"
CHARTS.mkdir(exist_ok=True)
SDK_ORDER = ["CSharpSDK", "NativeSDK", "NodeSDK", "CLI"]
SDK_LABEL = {"CSharpSDK": "C# SDK", "NativeSDK": "Native SDK", "NodeSDK": "Node.js SDK", "CLI": "CLI"}
PROFILE_ORDER = ["full", "heur", "ai", "sig", "aiscript"]
P_LABEL = {"full": "Sig + Heur + AI", "heur": "Sig + Heuristics", "ai": "Sig + AI", "sig": "Signatures only", "aiscript": "Full + AI Script"}
MB = 1048576
manifest = None


def load(tag):
    p = RAW / f"{tag}.json"
    return json.loads(p.read_text()) if p.exists() else None


def med(xs):
    return st.median(xs) if xs else None


def op(rec, name):
    return [o for o in rec["ops"] if o["op"] == name] if rec else []


def file_meta(path):
    name = path.replace("\\", "/").split("/")[-1]
    for f in manifest["files"]:
        if f["file"].split("/")[-1] == name:
            return f
    return None


def main():
    global manifest
    env = json.loads((RESULTS / "environment.json").read_text())
    from common import CORPUS
    manifest = json.loads((CORPUS / "manifest.json").read_text())
    summary = {"env": env, "mixed": manifest["mixed"], "files": manifest["files"]}

    # ---------------------------------------------------------- load
    loads = {}
    for sdk in SDK_ORDER:
        r = {}
        for kind in ["first", "steady"]:
            recs = [load(f"load_{kind}_{sdk}_{i}") for i in range(5)]
            recs = [x for x in recs if x and op(x, "load")]
            if not recs:
                continue
            base = [op(x, "baseline")[0]["ws_mb"] for x in recs]
            lo = [op(x, "load")[0] for x in recs]
            r[kind] = {
                "time_ms": med([o["times_ms"][0] for o in lo]),
                "cpu_ms": med([o["cpu_ms"] for o in lo]),
                "baseline_ws_mb": med(base),
                "ws_mb": med([o["ws_mb"] for o in lo]),
                "engine_ws_mb": med([o["ws_mb"] - b for o, b in zip(lo, base)]),
                "private_mb": med([o["private_mb"] for o in lo if o.get("private_mb") is not None]),
                "peak_ws_mb": med([o["peak_ws_mb"] for o in lo]),
                "process_peak_private_mb": med([x["process"]["peak_private_mb"] for x in recs]),
                "n": len(recs),
            }
        loads[sdk] = r
    summary["load"] = loads

    # ---------------------------------------------------------- per-profile
    prof = {}
    for p in PROFILE_ORDER:
        for sdk in SDK_ORDER:
            rec = load(f"profile_{p}_{sdk}")
            if not rec:
                continue
            base = op(rec, "baseline")[0]
            lo = op(rec, "load")[0]
            scans = {}
            for o in op(rec, "scan"):
                m = file_meta(o["path"])
                if not m:
                    continue
                key = m["file"].split("/")[-1]
                name, warm = o["result"].split("|warm=")
                t = sorted(o["times_ms"])
                scans[key] = {
                    "kind": m["kind"], "label": m["label"], "bytes": m["bytes"], "verdict": name,
                    "median_ms": med(t), "min_ms": t[0], "max_ms": t[-1],
                    "first_ms": float(warm.split(",")[0]) if warm else None,
                    "cpu_ms_per_scan": o["cpu_ms"] / len(t),
                    "mb_s": (m["bytes"] / MB) / (med(t) / 1000) if med(t) else None,
                }
            folder = op(rec, "folder")
            fo = None
            if folder:
                f = folder[-1]  # warm pass
                t = f["times_ms"][0]
                fo = {"time_ms": t, "files": f["files"], "files_s": f["files"] / (t / 1000),
                      "mb_s": manifest["mixed"]["bytes"] / MB / (t / 1000), "cpu_ms": f["cpu_ms"],
                      "cpu_pct_core": 100 * f["cpu_ms"] / t, "ws_mb": f["ws_mb"], "result": f["result"]}
            ups = op(rec, "update")
            up = None
            if ups:
                ts = [u["times_ms"][0] for u in ups]
                up = {"first_ms": ts[0], "median_ms": med(ts), "median_warm_ms": med(ts[1:]) if len(ts) > 1 else None,
                      "cpu_ms": med([u["cpu_ms"] for u in ups]), "result": ups[-1]["result"],
                      "ws_delta_mb": ups[-1]["ws_mb"] - ups[0]["ws_mb"]}
            prof.setdefault(p, {})[sdk] = {
                "baseline_ws_mb": base["ws_mb"], "load_ms": lo["times_ms"][0], "load_cpu_ms": lo["cpu_ms"],
                "ws_after_load_mb": lo["ws_mb"], "engine_ws_mb": lo["ws_mb"] - base["ws_mb"],
                "private_after_load_mb": lo.get("private_mb"),
                "peak_ws_mb": rec["process"]["peak_ws_mb"], "peak_private_mb": rec["process"]["peak_private_mb"],
                "process_cpu_ms": rec["process"]["cpu_ms"], "scans": scans, "folder": fo, "update": up,
            }
    summary["profiles"] = prof

    # ---------------------------------------------------------- CLI one-shot
    co = load("cli_oneshot")
    if co is None and (RAW / "cli_oneshot.json").exists():
        co = json.loads((RAW / "cli_oneshot.json").read_text())
    cli = {}
    for row in co or []:
        cli.setdefault(row["case"], []).append(row)
    summary["cli_oneshot"] = {k: {"wall_ms": med([r["wall_ms"] for r in v]), "cpu_ms": med([r["cpu_ms"] for r in v]),
                                  "peak_ws_mb": med([r["peak_ws_mb"] for r in v]), "peak_private_mb": med([r["peak_private_mb"] for r in v]),
                                  "n": len(v), "stdout": v[-1]["stdout"][:120]} for k, v in cli.items()}

    # ---------------------------------------------------------- scaling
    sc_rec = load("scaling_CSharpSDK")
    scaling = []
    if sc_rec:
        pf = op(sc_rec, "pfolder")[1:]  # skip warm-up pass
        for o in pf:
            t = o["times_ms"][0]
            scaling.append({"threads": o["threads"], "time_ms": t, "files_s": o["files"] / (t / 1000),
                            "mb_s": manifest["mixed"]["bytes"] / MB / (t / 1000), "cpu_pct_core": 100 * o["cpu_ms"] / t,
                            "ws_mb": o["ws_mb"]})
        summary["scaling_peak_ws_mb"] = sc_rec["process"]["peak_ws_mb"]
    summary["scaling"] = scaling

    # ---------------------------------------------------------- fresh updates
    fresh = {}
    for tag in ["fresh_update_full_aiscript", "fresh_update_default_NativeSDK", "fresh_update_default_NodeSDK"]:
        r = load(tag)
        if r:
            u = op(r, "update")[0]
            fresh[tag] = {"sdk": r["sdk"], "time_ms": u["times_ms"][0], "cpu_ms": u["cpu_ms"], "result": u["result"],
                          "peak_ws_mb": r["process"]["peak_ws_mb"], "peak_private_mb": r["process"]["peak_private_mb"]}
    summary["fresh_update"] = fresh

    (RESULTS / "summary.json").write_text(json.dumps(summary, indent=2))
    charts(summary)
    print("summary + charts written")


def charts(s):
    L = s["load"]
    P = s["profiles"]
    sdks = [x for x in SDK_ORDER if x in L]
    labels = [SDK_LABEL[x] for x in sdks]

    sc.hbar(CHARTS / "load_time.svg", "Engine load time", "Time for Load() to make the engine ready to scan · median, lower is better",
            labels, [("Steady state", [L[x].get("steady", {}).get("time_ms") for x in sdks]),
                     ("First run (no rules.json)", [L[x].get("first", {}).get("time_ms") for x in sdks])], unit=" ms")

    sc.hbar(CHARTS / "memory_load.svg", "Memory footprint after load", "Working set (MB) · host runtime baseline vs. total after the engine is loaded",
            labels, [("Host baseline (before load)", [L[x]["steady"]["baseline_ws_mb"] for x in sdks]),
                     ("After load", [L[x]["steady"]["ws_mb"] for x in sdks]),
                     ("Peak during full benchmark", [P["full"][x]["peak_ws_mb"] for x in sdks])], unit=" MB")

    # PE latency by size, per SDK (default profile)
    pe = sorted([f for f in s["files"] if f["kind"] == "pe"], key=lambda f: f["bytes"])
    xl = [f["label"] for f in pe]
    xv = [f["bytes"] for f in pe]
    sc.line(CHARTS / "pe_latency_by_sdk.svg", "PE scan time by file size", "Default engines · median per scan, log–log · files >20 MB skip heuristics/AI (MaxHeuristicsPeScanLength)",
            xl, xv, [(SDK_LABEL[x], [P["full"][x]["scans"][f["file"]]["median_ms"] for f in pe]) for x in sdks], unit=" ms", y_label="milliseconds")

    eng = [p for p in ["full", "heur", "ai", "sig"] if p in P]
    sc.line(CHARTS / "pe_latency_by_engine.svg", "PE scan time by engine configuration", "C# SDK · median per scan, log–log · files >20 MB are hash-checked only",
            xl, xv, [(P_LABEL[p], [P[p]["CSharpSDK"]["scans"][f["file"]]["median_ms"] for f in pe]) for p in eng], unit=" ms", y_label="milliseconds")

    for kind, title in [("text", "Non-PE (text) scan time by file size"), ("binary", "Non-PE (binary) scan time by file size")]:
        nf = sorted([f for f in s["files"] if f["kind"] == kind], key=lambda f: f["bytes"])
        sc.line(CHARTS / f"nonpe_{kind}_latency_by_engine.svg", title, "C# SDK · median, log–log · AI applies to PE only (lines overlap) · heuristics stop at 10 MB",
                [f["label"] for f in nf], [f["bytes"] for f in nf],
                [(P_LABEL[p], [P[p]["CSharpSDK"]["scans"][f["file"]]["median_ms"] for f in nf]) for p in eng], unit=" ms", y_label="milliseconds")

    # Throughput at 8MB
    cats = ["PE 8 MB", "PE 2 MB", "Text 8 MB", "Binary 8 MB"]
    keys = ["pe_8MB.dll", "pe_2MB.dll", "text_8MB.log", "binary_8MB.bin"]
    sc.hbar(CHARTS / "throughput_by_engine.svg", "Single-file scan throughput", "C# SDK · MB/s of file content scanned per second (single thread) · higher is better",
            cats, [(P_LABEL[p], [P[p]["CSharpSDK"]["scans"][k]["mb_s"] for k in keys]) for p in eng], unit=" MB/s")

    # Folder throughput per SDK x engine
    sc.hbar(CHARTS / "folder_files_per_sec.svg", f"Folder scan throughput ({s['mixed']['count']:,} mixed files)",
            "Files per second, single thread, one Scan() per file · higher is better",
            [P_LABEL[p] for p in eng], [(SDK_LABEL[x], [P[p][x]["folder"]["files_s"] if P[p][x]["folder"] else None for p in eng]) for x in sdks], unit=" files/s")

    sc.hbar(CHARTS / "folder_cpu.svg", "CPU usage during folder scan", "CPU time ÷ wall time, % of one logical core (100% = one core fully busy)",
            [P_LABEL[p] for p in eng], [(SDK_LABEL[x], [P[p][x]["folder"]["cpu_pct_core"] for p in eng]) for x in sdks], unit="%")

    # Updates
    upd = [x for x in sdks if P["full"][x]["update"]]
    sc.hbar(CHARTS / "update_check.svg", "Check for updates (database already current)", "Round-trip to cloud.xvirus.net incl. version comparison · median of 5 · lower is better",
            [SDK_LABEL[x] for x in upd], [("First call in process", [P["full"][x]["update"]["first_ms"] for x in upd]),
                                          ("Subsequent calls (median)", [P["full"][x]["update"]["median_warm_ms"] for x in upd])], unit=" ms")

    # Scaling
    if s["scaling"]:
        sc.line(CHARTS / "thread_scaling.svg", "Multi-threaded scan scaling", "C# SDK · mixed folder, Parallel.ForEach over Scan() · files per second",
                [str(r["threads"]) + "T" for r in s["scaling"]], [r["threads"] for r in s["scaling"]],
                [("C# SDK", [r["files_s"] for r in s["scaling"]])], logx=False, logy=False, y_label="files per second")

    # AI script
    if "aiscript" in P:
        scripts = [f for f in s["files"] if f["kind"] == "script"]
        names = [f["file"].split("/")[-1] for f in scripts]
        sc.hbar(CHARTS / "ai_script.svg", "AI Script (LLM) scan time per script", "Median per scan, seconds · runs fully on the CPU · lower is better",
                names, [(SDK_LABEL[x], [(P["aiscript"][x]["scans"].get(n) or {}).get("median_ms", 0) / 1000 or None for n in names]) for x in sdks],
                unit=" s")

    # CLI modes
    co = s["cli_oneshot"]
    if co:
        cases = [("scan_pe_2MB", "pe_2MB.dll", "PE 2 MB"), ("scan_pe_512KB", "pe_512KB.dll", "PE 512 KB"), ("scan_text_64KB", "text_64KB.log", "Text 64 KB")]
        sc.hbar(CHARTS / "cli_modes.svg", "CLI: one-shot vs. interactive mode", "Wall time per scan (ms) · one-shot includes process start + engine load · log scale",
                [c[2] for c in cases], [("One-shot (XvirusCLI s <file>)", [co[c[0]]["wall_ms"] for c in cases]),
                                        ("Interactive (engine kept loaded)", [P["full"]["CLI"]["scans"][c[1]]["median_ms"] for c in cases])], unit=" ms", log=True)

    # Report banner with headline KPIs
    nat = L["NativeSDK"]["steady"]
    sc.banner(CHARTS / "banner.svg", "SDK & CLI Performance Report",
              "SDK & CLI 5.2.2 · BENCHMARKED " + s["env"]["date"],
              [(f"{nat['time_ms']:.0f} ms", "Engine load (Native SDK)"),
               (f"{P['full']['NativeSDK']['scans']['signed_pe.exe']['median_ms']:.1f} ms", "Signed file verdict"),
               (f"{P['full']['NativeSDK']['scans']['pe_2MB.dll']['median_ms']:.0f} ms", "2 MB PE, all engines"),
               (f"{max(r['files_s'] for r in s['scaling']):,.0f}", "Files/s at 12 threads"),
               (f"{P['full']['NativeSDK']['update']['median_warm_ms']:.0f} ms", "Update check")],
              logo_path=CHARTS.parent.parent / "Website" / "public" / "logo.png")


if __name__ == "__main__":
    main()
