"""Minimal dependency-free SVG chart renderer with Xvirus branding."""
import math
from html import escape

FONT = "Segoe UI, Inter, Helvetica, Arial, sans-serif"
INK = "#0B1220"
INK2 = "#4A5568"
MUTED = "#8A94A6"
GRID = "#E6E9EF"
SURFACE = "#FFFFFF"
BRAND = "#00A651"

# Fixed categorical order (validated): green, blue, amber, violet, red.
SERIES = ["#00A651", "#2F6FDB", "#E8890C", "#8A3FC7", "#D64550"]


def _fmt(v, unit=""):
    if v is None:
        return "–"
    if v == 0:
        return "0" + unit
    if abs(v) >= 1000:
        s = f"{v:,.0f}"
    elif abs(v) >= 100:
        s = f"{v:.0f}"
    elif abs(v) >= 10:
        s = f"{v:.1f}"
    else:
        s = f"{v:.2f}"
    return s + unit


def _frame(w, h, title, subtitle):
    out = [f'<svg xmlns="http://www.w3.org/2000/svg" width="{w}" height="{h}" viewBox="0 0 {w} {h}" font-family="{FONT}">',
           f'<rect x="0.5" y="0.5" width="{w-1}" height="{h-1}" rx="10" fill="{SURFACE}" stroke="{GRID}"/>',
           f'<rect x="0" y="0" width="6" height="{h}" rx="3" fill="{BRAND}"/>',
           f'<text x="28" y="34" font-size="17" font-weight="700" fill="{INK}">{escape(title)}</text>']
    if subtitle:
        out.append(f'<text x="28" y="55" font-size="12.5" fill="{INK2}">{escape(subtitle)}</text>')
    out.append(f'<text x="{w-20}" y="{h-12}" font-size="10.5" font-weight="700" fill="{BRAND}" text-anchor="end" letter-spacing="1">XVIRUS</text>')
    return out


def _legend(out, series_names, x, y, colors):
    cx = x
    for i, name in enumerate(series_names):
        out.append(f'<rect x="{cx}" y="{y-9}" width="11" height="11" rx="3" fill="{colors[i]}"/>')
        out.append(f'<text x="{cx+16}" y="{y}" font-size="12" fill="{INK2}">{escape(name)}</text>')
        cx += 16 + 7.2 * len(name) + 22


def hbar(path, title, subtitle, categories, series, unit="", width=860, log=False, value_fmt=None):
    """Horizontal grouped bars. series: list of (name, [values per category])."""
    n_s = len(series)
    bar_h, gap, group_gap = 16, 2, 18
    group_h = n_s * bar_h + (n_s - 1) * gap
    top = 102 if n_s > 1 else 80
    left, right = 190, 90
    h = top + len(categories) * (group_h + group_gap) + 40
    out = _frame(width, h, title, subtitle)
    if n_s > 1:
        _legend(out, [s[0] for s in series], 28, 80, SERIES)
    vals = [v for _, vs in series for v in vs if v is not None]
    vmax = max(vals) if vals else 1
    plot_w = width - left - right

    def sx(v):
        if log:
            lo = 10 ** math.floor(math.log10(max(min(vals), 1e-3)))
            hi = 10 ** math.ceil(math.log10(vmax))
            return plot_w * (math.log10(max(v, lo)) - math.log10(lo)) / (math.log10(hi) - math.log10(lo))
        return plot_w * v / (vmax * 1.05)

    # recessive grid
    if not log:
        step = _nice_step(vmax * 1.05, 5)
        t = 0.0
        while t <= vmax * 1.05:
            x = left + sx(t)
            out.append(f'<line x1="{x:.1f}" y1="{top-6}" x2="{x:.1f}" y2="{h-34}" stroke="{GRID}"/>')
            out.append(f'<text x="{x:.1f}" y="{h-20}" font-size="10.5" fill="{MUTED}" text-anchor="middle">{_fmt(t)}</text>')
            t += step
    out.append(f'<line x1="{left}" y1="{top-6}" x2="{left}" y2="{h-34}" stroke="{MUTED}"/>')

    y = top
    for ci, cat in enumerate(categories):
        out.append(f'<text x="{left-12}" y="{y + group_h/2 + 4:.1f}" font-size="12.5" fill="{INK}" text-anchor="end">{escape(cat)}</text>')
        for si, (name, vs) in enumerate(series):
            v = vs[ci]
            by = y + si * (bar_h + gap)
            if v is None:
                out.append(f'<text x="{left+6}" y="{by+12}" font-size="11" fill="{MUTED}">n/a</text>')
                continue
            bw = max(sx(v), 2)
            color = SERIES[si] if n_s > 1 else BRAND
            out.append(f'<rect x="{left}" y="{by}" width="{bw:.1f}" height="{bar_h}" rx="4" fill="{color}"><title>{escape(name)} · {escape(cat)}: {_fmt(v, unit)}</title></rect>')
            label = value_fmt(v) if value_fmt else _fmt(v, unit)
            out.append(f'<text x="{left + bw + 6:.1f}" y="{by+12.5}" font-size="11.5" fill="{INK2}">{escape(label)}</text>')
        y += group_h + group_gap
    out.append("</svg>")
    open(path, "w", encoding="utf-8").write("\n".join(out))


def _nice_step(span, n):
    raw = span / n
    mag = 10 ** math.floor(math.log10(raw))
    for m in (1, 2, 2.5, 5, 10):
        if raw <= m * mag:
            return m * mag
    return 10 * mag


def line(path, title, subtitle, x_labels, x_values, series, unit="", width=860, height=420, logx=True, logy=True,
         y_label=""):
    """series: list of (name, [y per x])"""
    top = 92 if len(series) > 1 else 74
    left, right, bottom = 78, 140, 58
    out = _frame(width, height, title, subtitle)
    if len(series) > 1:
        _legend(out, [s[0] for s in series], 28, 78, SERIES)
    pw, ph = width - left - right, height - top - bottom
    ys = [v for _, vs in series for v in vs if v]
    ylo, yhi = min(ys), max(ys)
    if logy:
        ylo = 10 ** math.floor(math.log10(ylo))
        yhi = 10 ** math.ceil(math.log10(yhi))
    else:
        ylo, yhi = 0, yhi * 1.1
    xlo, xhi = min(x_values), max(x_values)

    def X(v):
        return left + pw * ((math.log10(v) - math.log10(xlo)) / (math.log10(xhi) - math.log10(xlo)) if logx else (v - xlo) / (xhi - xlo))

    def Y(v):
        if logy:
            return top + ph - ph * (math.log10(v) - math.log10(ylo)) / (math.log10(yhi) - math.log10(ylo))
        return top + ph - ph * (v - ylo) / (yhi - ylo)

    # grid
    if logy:
        e = math.log10(ylo)
        while e <= math.log10(yhi) + 1e-9:
            v = 10 ** e
            out.append(f'<line x1="{left}" y1="{Y(v):.1f}" x2="{left+pw}" y2="{Y(v):.1f}" stroke="{GRID}"/>')
            out.append(f'<text x="{left-8}" y="{Y(v)+4:.1f}" font-size="10.5" fill="{MUTED}" text-anchor="end">{_fmt(v)}{unit}</text>')
            e += 1
    else:
        step = _nice_step(yhi, 5)
        v = 0
        while v <= yhi:
            out.append(f'<line x1="{left}" y1="{Y(v):.1f}" x2="{left+pw}" y2="{Y(v):.1f}" stroke="{GRID}"/>')
            out.append(f'<text x="{left-8}" y="{Y(v)+4:.1f}" font-size="10.5" fill="{MUTED}" text-anchor="end">{_fmt(v)}</text>')
            v += step
    for xl, xv in zip(x_labels, x_values):
        out.append(f'<text x="{X(xv):.1f}" y="{top+ph+18}" font-size="10.5" fill="{MUTED}" text-anchor="middle">{escape(xl)}</text>')
    out.append(f'<line x1="{left}" y1="{top+ph}" x2="{left+pw}" y2="{top+ph}" stroke="{MUTED}"/>')
    if y_label:
        out.append(f'<text x="18" y="{top+ph/2}" font-size="11" fill="{INK2}" transform="rotate(-90 18 {top+ph/2})" text-anchor="middle">{escape(y_label)}</text>')

    # lines + markers + end labels (selective direct labels)
    end_labels = []
    for si, (name, vs) in enumerate(series):
        c = SERIES[si]
        pts = [(X(x), Y(v), v) for x, v in zip(x_values, vs) if v]
        out.append(f'<polyline fill="none" stroke="{c}" stroke-width="2" stroke-linejoin="round" points="{" ".join(f"{a:.1f},{b:.1f}" for a, b, _ in pts)}"/>')
        for (a, b, v), xl in zip(pts, [l for l, v in zip(x_labels, vs) if v]):
            out.append(f'<circle cx="{a:.1f}" cy="{b:.1f}" r="4" fill="{c}" stroke="{SURFACE}" stroke-width="2"><title>{escape(name)} · {escape(xl)}: {_fmt(v, unit)}</title></circle>')
        if pts:
            end_labels.append([pts[-1][1], f"{name}: {_fmt(pts[-1][2], unit)}", pts[-1][0]])
    end_labels.sort()
    for i in range(1, len(end_labels)):
        if end_labels[i][0] - end_labels[i - 1][0] < 14:
            end_labels[i][0] = end_labels[i - 1][0] + 14
    for yy, txt, xx in end_labels:
        out.append(f'<text x="{xx+9:.1f}" y="{yy+4:.1f}" font-size="11" fill="{INK2}">{escape(txt)}</text>')
    out.append("</svg>")
    open(path, "w", encoding="utf-8").write("\n".join(out))


def banner(path, title, subtitle, stats, width=960, logo_path=None):
    """Report header: brand bar + headline KPI tiles. stats: list of (value, label)."""
    h = 210
    out = [f'<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{h}" viewBox="0 0 {width} {h}" font-family="{FONT}">',
           '<defs><linearGradient id="g" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#0B1220"/><stop offset="1" stop-color="#0F2A1E"/></linearGradient></defs>',
           f'<rect width="{width}" height="{h}" rx="14" fill="url(#g)"/>',
           _logo(logo_path),
           f'<text x="{150 if logo_path else 132}" y="84" font-size="13" fill="#9FB3C8" letter-spacing="1">{escape(subtitle)}</text>',
           f'<text x="{width-34}" y="62" font-size="18" font-weight="700" fill="#FFFFFF" text-anchor="end">{escape(title)}</text>']
    n = len(stats)
    tw = (width - 68 - (n - 1) * 14) / n
    for i, (val, lab) in enumerate(stats):
        x = 34 + i * (tw + 14)
        out.append(f'<rect x="{x:.1f}" y="112" width="{tw:.1f}" height="70" rx="10" fill="#FFFFFF" fill-opacity="0.06" stroke="#00E676" stroke-opacity="0.25"/>')
        out.append(f'<text x="{x+16:.1f}" y="146" font-size="24" font-weight="800" fill="#00E676">{escape(val)}</text>')
        out.append(f'<text x="{x+16:.1f}" y="168" font-size="12" fill="#C9D6E3">{escape(lab)}</text>')
    out.append("</svg>")
    open(path, "w", encoding="utf-8").write("\n".join(out))


def _logo(logo_path):
    """Official logo PNG embedded as a data URI; falls back to a wordmark."""
    if logo_path:
        import base64
        b64 = base64.b64encode(open(logo_path, "rb").read()).decode()
        return f'<image x="30" y="14" width="107" height="80" href="data:image/png;base64,{b64}"/>'                f'<text x="150" y="62" font-size="15" font-weight="700" fill="#FFFFFF" letter-spacing="2">SECURITY ENGINE</text>'
    return f'<text x="34" y="62" font-size="30" font-weight="800" fill="{BRAND}" letter-spacing="3">XVIRUS</text>'
