"""Render Benchmarks/BENCHMARK_REPORT.md to a branded PDF with headless Microsoft Edge.

Dependency-free: a small Markdown converter covering what the report uses (headings, pipe tables,
lists, fenced code, images, links, inline emphasis/code and raw HTML blocks), print CSS with the
Xvirus palette, then Edge's --print-to-pdf (SVG charts render natively).

Usage: py -3 make_pdf.py [path\\to\\msedge.exe]
"""
import html
import os
import re
import subprocess
import sys
import tempfile
from pathlib import Path

BENCH = Path(__file__).resolve().parent.parent
SRC = BENCH / "BENCHMARK_REPORT.md"
OUT = BENCH / "BENCHMARK_REPORT.pdf"
EDGE_CANDIDATES = [
    r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
    r"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
]

CSS = """
@page { size: A4; margin: 16mm 14mm 18mm 14mm;
  @bottom-left { content: "Xvirus SDK & CLI 5.2.2 Performance Report"; font: 8pt 'Segoe UI', sans-serif; color: #8A94A6; }
  @bottom-right { content: "Page " counter(page) " of " counter(pages); font: 8pt 'Segoe UI', sans-serif; color: #8A94A6; } }
@page :first { @bottom-left { content: none; } @bottom-right { content: none; } }
* { box-sizing: border-box; }
html { -webkit-print-color-adjust: exact; print-color-adjust: exact; }
body { font: 9.6pt/1.5 'Segoe UI', Inter, Helvetica, Arial, sans-serif; color: #1F2937; margin: 0; }
h1 { font-size: 20pt; color: #0B1220; margin: 14px 0 10px; line-height: 1.2; }
h2 { font-size: 15pt; color: #0B1220; margin: 0 0 10px; padding: 0 0 6px 12px;
     border-left: 5px solid #00A651; border-bottom: 1px solid #E6E9EF; break-before: page; break-after: avoid; }
h3 { font-size: 11.5pt; color: #0B1220; margin: 16px 0 6px; break-after: avoid; }
p { margin: 6px 0; }
a { color: #00804A; text-decoration: none; }
strong { color: #0B1220; }
ul, ol { margin: 6px 0; padding-left: 20px; }
li { margin: 3px 0; }
code { font: 8.6pt Consolas, 'Cascadia Mono', monospace; background: #F1F5F3; color: #0B3D26; padding: 1px 4px; border-radius: 3px; }
pre { background: #0B1220; color: #E6EDF3; padding: 10px 12px; border-radius: 8px; font: 8pt/1.45 Consolas, monospace;
      white-space: pre-wrap; break-inside: avoid; border-left: 4px solid #00A651; }
pre code { background: none; color: inherit; padding: 0; font: inherit; }
table { width: 100%; border-collapse: collapse; margin: 8px 0 10px; font-size: 8.4pt; break-inside: auto; }
thead { display: table-header-group; }
tr { break-inside: avoid; }
th { background: #0B1220; color: #FFFFFF; font-weight: 600; text-align: left; padding: 5px 7px; }
td { padding: 4px 7px; border-bottom: 1px solid #E6E9EF; vertical-align: top; }
tbody tr:nth-child(even) td { background: #F6F8F7; }
th.r, td.r { text-align: right; } th.c, td.c { text-align: center; }
img { max-width: 100%; height: auto; }
.figure { margin: 10px 0 12px; break-inside: avoid; text-align: center; }
.figure img { max-height: 92mm; width: auto; }
.center { text-align: center; }
.note { color: #4A5568; font-size: 8.4pt; }
.path { font: 8.6pt Consolas, monospace; color: #0B3D26; }
h2#contents { break-before: auto; margin-top: 18px; }
table.kv td:first-child { width: 22%; font-weight: 600; color: #0B1220; white-space: nowrap; }
table.kv tbody tr:nth-child(even) td { background: none; }
table.kv { border-top: 3px solid #00A651; break-inside: avoid; }
"""


def slug(text):
    t = re.sub(r"<[^>]+>", "", text).lower()
    t = re.sub(r"[^a-z0-9 -]", "", t)
    return t.replace(" ", "-")


def inline(s):
    codes = []

    def keep_code(m):
        codes.append(f"<code>{html.escape(m.group(1))}</code>")
        return f"\x00{len(codes) - 1}\x00"

    s = re.sub(r"`([^`]+)`", keep_code, s)
    # Escape everything except entities the source already uses (&amp; etc.).
    s = html.escape(s, quote=False).replace("&amp;amp;", "&amp;")
    s = re.sub(r"!\[([^\]]*)\]\(([^)]+)\)", r'<img alt="\1" src="\2">', s)

    def link(m):
        text, href = m.group(1), m.group(2)
        if href.startswith("#") or href.startswith("http"):
            return f'<a href="{href}">{text}</a>'
        return f'<span class="path">{text}</span>'  # repository-relative paths: shown, not linked

    s = re.sub(r"\[([^\]]+)\]\(([^)]+)\)", link, s)
    s = re.sub(r"\*\*(.+?)\*\*", r"<strong>\1</strong>", s)
    s = re.sub(r"(?<![\w*])\*(?!\s)(.+?)(?<!\s)\*(?![\w*])", r"<em>\1</em>", s)
    return re.sub(r"\x00(\d+)\x00", lambda m: codes[int(m.group(1))], s)


def table(lines):
    rows = [[c.strip() for c in l.strip().strip("|").split("|")] for l in lines]
    aligns = []
    for c in rows[1]:
        aligns.append("c" if c.startswith(":") and c.endswith(":") else "r" if c.endswith(":") else "")
    body = []
    for r in rows[2:]:
        body.append("<tr>" + "".join(f'<td class="{aligns[i] if i < len(aligns) else ""}">{inline(c)}</td>'
                                     for i, c in enumerate(r)) + "</tr>")
    if not any(rows[0]):  # "| | |" header: a key/value table without a heading row
        return f"<table class=\"kv\"><tbody>{''.join(body)}</tbody></table>"
    head = "".join(f'<th class="{aligns[i]}">{inline(c)}</th>' for i, c in enumerate(rows[0]))
    return f"<table><thead><tr>{head}</tr></thead><tbody>{''.join(body)}</tbody></table>"


def convert(md):
    lines = md.splitlines()
    out, i = [], 0
    while i < len(lines):
        line = lines[i]
        st = line.strip()
        if not st or st == "---":
            i += 1
        elif st.startswith("```"):
            j = i + 1
            while not lines[j].strip().startswith("```"):
                j += 1
            out.append("<pre><code>" + html.escape("\n".join(lines[i + 1:j])) + "</code></pre>")
            i = j + 1
        elif st.startswith("<"):  # raw HTML block (banner, footer)
            j = i
            while j < len(lines) and lines[j].strip():
                j += 1
            block = "\n".join(lines[i:j]).replace('<p align="center">', '<p class="center">')
            out.append(block)
            i = j
        elif m := re.match(r"^(#{1,6}) (.+)$", st):
            n, text = len(m.group(1)), m.group(2)
            out.append(f'<h{n} id="{slug(text)}">{inline(text)}</h{n}>')
            i += 1
        elif st.startswith("|") and i + 1 < len(lines) and re.match(r"^\|[-:| ]+\|$", lines[i + 1].strip()):
            j = i
            while j < len(lines) and lines[j].strip().startswith("|"):
                j += 1
            out.append(table(lines[i:j]))
            i = j
        elif re.match(r"^(- |\d+\. )", st):
            ordered = bool(re.match(r"^\d+\. ", st))
            items, j = [], i
            while j < len(lines) and (re.match(r"^(- |\d+\. )", lines[j].strip()) or (lines[j].startswith("  ") and lines[j].strip())):
                if re.match(r"^(- |\d+\. )", lines[j].strip()):
                    items.append(re.sub(r"^(- |\d+\. )", "", lines[j].strip()))
                else:
                    items[-1] += " " + lines[j].strip()
                j += 1
            tag = "ol" if ordered else "ul"
            out.append(f"<{tag}>" + "".join(f"<li>{inline(x)}</li>" for x in items) + f"</{tag}>")
            i = j
        elif re.match(r"^!\[[^\]]*\]\([^)]+\)$", st):
            out.append(f'<div class="figure">{inline(st)}</div>')
            i += 1
        else:
            j = i
            para = []
            while j < len(lines) and lines[j].strip() and not re.match(r"^(#|\||```|- |\d+\. |<|!\[)", lines[j].strip()):
                para.append(lines[j].strip())
                j += 1
            text = " ".join(para)
            cls = ' class="note"' if text.startswith("*") and text.endswith("*") and not text.startswith("**") else ""
            out.append(f"<p{cls}>{inline(text)}</p>")
            i = j
    return "\n".join(out)


def main():
    edge = next((p for p in ([sys.argv[1]] if len(sys.argv) > 1 else []) + EDGE_CANDIDATES if os.path.exists(p)), None)
    if not edge:
        sys.exit("Microsoft Edge not found; pass the path to msedge.exe")
    body = convert(SRC.read_text(encoding="utf-8"))
    base = BENCH.as_uri() + "/"
    doc = (f'<!doctype html><html lang="en"><head><meta charset="utf-8"><base href="{base}">'
           f"<title>Xvirus SDK &amp; CLI 5.2.2 Performance Report</title><style>{CSS}</style></head>"
           f"<body>{body}</body></html>")
    with tempfile.TemporaryDirectory() as tmp:
        page = Path(tmp) / "report.html"
        page.write_text(doc, encoding="utf-8")
        subprocess.run([edge, "--headless=new", "--disable-gpu", "--no-pdf-header-footer",
                        f"--user-data-dir={Path(tmp) / 'profile'}", f"--print-to-pdf={OUT}", page.as_uri()],
                       check=True, capture_output=True, timeout=180)
    print(f"wrote {OUT} ({OUT.stat().st_size / 1024:.0f} KB)")


if __name__ == "__main__":
    main()
