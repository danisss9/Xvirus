"""Builds the benchmark corpus.

Every file is made unique (a random 32-byte tail is appended) so no file can be resolved by a
whitelist/blacklist hash hit: each scan exercises the full engine pipeline (worst case).
"""
import json
import os
import random
import shutil
import struct
from pathlib import Path

from common import CORPUS

random.seed(20261125)
SYS32 = Path(os.environ["WINDIR"]) / "System32"
MB = 1048576


def has_embedded_signature(p: Path) -> bool:
    try:
        with open(p, "rb") as f:
            d = f.read(4096)
        if d[:2] != b"MZ":
            return True
        pe = struct.unpack_from("<I", d, 0x3C)[0]
        if d[pe:pe + 4] != b"PE\0\0":
            return True
        magic = struct.unpack_from("<H", d, pe + 24)[0]
        off = pe + 24 + (144 if magic == 0x20B else 128)
        _, size = struct.unpack_from("<II", d, off)
        return size != 0
    except Exception:
        return True


def unique_tail() -> bytes:
    return random.randbytes(32)


def pick_pe(target: int, used: set) -> Path:
    best = None
    for p in SYS32.glob("*.dll"):
        try:
            sz = p.stat().st_size
        except OSError:
            continue
        if p in used or has_embedded_signature(p):
            continue
        if best is None or abs(sz - target) < abs(best[1] - target):
            best = (p, sz)
    used.add(best[0])
    return best[0]


def build_pe(dst: Path, target: int, used: set) -> dict:
    src = pick_pe(target, used)
    data = src.read_bytes()
    if len(data) < target:
        # Grow with an overlay made of real code bytes from other system binaries.
        donors = sorted(SYS32.glob("*.dll"), key=lambda p: -p.stat().st_size)[:12]
        buf = bytearray(data)
        while len(buf) < target:
            buf += donors[random.randrange(len(donors))].read_bytes()[: target - len(buf)]
        data = bytes(buf)
    dst.write_bytes(data + unique_tail())
    return {"file": dst.name, "kind": "pe", "bytes": dst.stat().st_size, "source": src.name}


LOREM = ("timestamp level component message user_id request_id latency status payload "
         "invoice customer order shipment address product quantity price total discount ").split()


def text_blob(n: int) -> bytes:
    out, size = [], 0
    while size < n:
        rec = json.dumps({"ts": random.randint(1_600_000_000, 1_800_000_000), "lvl": random.choice(["INFO", "WARN", "DEBUG"]),
                          "msg": " ".join(random.choices(LOREM, k=random.randint(6, 18))),
                          "id": random.getrandbits(64)}) + "\n"
        out.append(rec)
        size += len(rec)
    return "".join(out).encode()[:n]


def build_nonpe(dst: Path, target: int, kind: str) -> dict:
    data = text_blob(target - 32) if kind == "text" else random.randbytes(target - 32)
    dst.write_bytes(data + unique_tail())
    return {"file": dst.name, "kind": kind, "bytes": dst.stat().st_size}


SCRIPTS = {
    "backup.ps1": """# Nightly backup of the reports folder
param([string]$Source = "C:\\Reports", [string]$Dest = "D:\\Backup")
$stamp = Get-Date -Format "yyyyMMdd"
$target = Join-Path $Dest "reports-$stamp"
New-Item -ItemType Directory -Force -Path $target | Out-Null
Get-ChildItem $Source -Recurse -File | Where-Object { $_.LastWriteTime -gt (Get-Date).AddDays(-1) } |
    ForEach-Object { Copy-Item $_.FullName -Destination $target -Force }
Write-Host "Backup completed to $target"
""",
    "build.bat": """@echo off
setlocal
set CONFIG=Release
echo Building solution in %CONFIG% mode...
dotnet restore
dotnet build -c %CONFIG% --no-restore
if errorlevel 1 (
  echo Build failed
  exit /b 1
)
dotnet test -c %CONFIG% --no-build
echo Done.
""",
    "server.js": """const http = require('http');
const url = require('url');
const port = process.env.PORT || 8080;
const routes = { '/health': () => ({ status: 'ok', uptime: process.uptime() }), '/time': () => ({ now: new Date().toISOString() }) };
http.createServer((req, res) => {
  const { pathname } = url.parse(req.url);
  const handler = routes[pathname];
  res.setHeader('Content-Type', 'application/json');
  if (!handler) { res.statusCode = 404; return res.end(JSON.stringify({ error: 'not found' })); }
  res.end(JSON.stringify(handler()));
}).listen(port, () => console.log(`listening on ${port}`));
""",
    "report.py": """import csv
import statistics
from pathlib import Path

def summarize(path):
    with open(path, newline='') as f:
        rows = list(csv.DictReader(f))
    totals = [float(r['amount']) for r in rows]
    return {'count': len(totals), 'sum': sum(totals), 'mean': statistics.mean(totals) if totals else 0}

if __name__ == '__main__':
    for p in Path('data').glob('*.csv'):
        print(p.name, summarize(p))
""",
}


def main():
    if CORPUS.exists():
        shutil.rmtree(CORPUS)
    files = CORPUS / "files"
    files.mkdir(parents=True)
    manifest = []
    used = set()

    for label, size in [("16KB", 16 * 1024), ("128KB", 128 * 1024), ("512KB", 512 * 1024), ("2MB", 2 * MB),
                        ("8MB", 8 * MB), ("18MB", 18 * MB), ("32MB", 32 * MB)]:
        m = build_pe(files / f"pe_{label}.dll", size, used)
        m["label"] = label
        manifest.append(m)

    for label, size in [("4KB", 4 * 1024), ("64KB", 64 * 1024), ("512KB", 512 * 1024), ("2MB", 2 * MB),
                        ("8MB", 8 * MB), ("32MB", 32 * MB)]:
        for kind, ext in [("text", "log"), ("binary", "bin")]:
            m = build_nonpe(files / f"{kind}_{label}.{ext}", size, kind)
            m["label"] = label
            manifest.append(m)

    # Authenticode-signed PE (exercises the signature fast path).
    signed_src = Path(os.environ["ProgramFiles"]) / "dotnet" / "dotnet.exe"
    if signed_src.exists():
        dst = files / "signed_pe.exe"
        dst.write_bytes(signed_src.read_bytes())  # unmodified: keeps the Authenticode blob parseable
        manifest.append({"file": dst.name, "kind": "signed-pe", "bytes": dst.stat().st_size, "label": "signed", "source": "dotnet.exe"})

    scripts = CORPUS / "scripts"
    scripts.mkdir()
    for name, body in SCRIPTS.items():
        (scripts / name).write_text(body + f"\n# {random.getrandbits(64):x}\n" if not name.endswith(".js")
                                    else body + f"\n// {random.getrandbits(64):x}\n")
        manifest.append({"file": "../scripts/" + name, "kind": "script", "bytes": (scripts / name).stat().st_size, "label": name})

    # Mixed folder: real-world-like workload of PE binaries and non-PE (source/assets) files.
    mixed = CORPUS / "mixed"
    mixed.mkdir()
    pes = [p for p in SYS32.glob("*.dll") if 4096 < p.stat().st_size < 6 * MB]
    random.shuffle(pes)
    for p in pes[:250]:
        try:
            (mixed / ("pe_" + p.name)).write_bytes(p.read_bytes() + unique_tail())
        except OSError:
            pass
    nm = Path(__file__).resolve().parents[2] / "XvirusInstaller" / "node_modules"
    others = [p for p in nm.rglob("*") if p.is_file() and p.suffix.lower() in {".js", ".json", ".md", ".ts", ".map", ".css", ".txt"}]
    random.shuffle(others)
    for i, p in enumerate(others[:750]):
        (mixed / f"f{i:04d}_{p.name}").write_bytes(p.read_bytes() + b"\n" + random.randbytes(16).hex().encode())

    mixed_files = list(mixed.iterdir())
    summary = {
        "files": manifest,
        "mixed": {
            "count": len(mixed_files),
            "pe": sum(1 for f in mixed_files if f.name.startswith("pe_")),
            "bytes": sum(f.stat().st_size for f in mixed_files),
        },
    }
    (CORPUS / "manifest.json").write_text(json.dumps(summary, indent=2))
    print(json.dumps(summary["mixed"]), len(manifest), "single files")


if __name__ == "__main__":
    main()
