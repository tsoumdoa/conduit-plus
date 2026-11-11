"""Diagram palette styles for Conduit+: curated bases, deterministic farthest-point
extension in OKLCH, metrics (OKLab dE, CVD, contrast) and swatch sheets (PNG + HTML).
Run: python make_palettes.py            (writes next to this file)"""
import math, os, sys, json
from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
PAPER, NIGHT = "#FFFFFF", "#1A1D23"
CAP = 24
MIN_DE = 5.0

# ---------- colour maths ----------
def hex2rgb(h): h = h.lstrip('#'); return tuple(int(h[i:i+2], 16) for i in (0, 2, 4))
def rgb2hex(r): return '#%02X%02X%02X' % tuple(r)
def lin(c): c /= 255; return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4
def unlin(c): return 12.92 * c if c <= 0.0031308 else 1.055 * c ** (1 / 2.4) - 0.055
def cbrt(x): return math.copysign(abs(x) ** (1 / 3), x)
def lin2oklab(r, g, b):
    l = cbrt(0.4122214708*r + 0.5363325363*g + 0.0514459929*b)
    m = cbrt(0.2119034982*r + 0.6806995451*g + 0.1073969566*b)
    s = cbrt(0.0883024619*r + 0.2817188376*g + 0.6299787005*b)
    return (0.2104542553*l + 0.7936177850*m - 0.0040720468*s,
            1.9779984951*l - 2.4285922050*m + 0.4505937099*s,
            0.0259040371*l + 0.7827717662*m - 0.8086757660*s)
def oklab2lin(L, a, b):
    l = (L + 0.3963377774*a + 0.2158037573*b) ** 3
    m = (L - 0.1055613458*a - 0.0638541728*b) ** 3
    s = (L - 0.0894841775*a - 1.2914855480*b) ** 3
    return (4.0767416621*l - 3.3077115913*m + 0.2309699292*s,
            -1.2684380046*l + 2.6097574011*m - 0.3413193965*s,
            -0.0041960863*l - 0.7034186147*m + 1.7076147010*s)
def oklab(h): return lin2oklab(*[lin(c) for c in hex2rgb(h)])
def oklch(h):
    L, a, b = oklab(h); return L, math.hypot(a, b), math.degrees(math.atan2(b, a)) % 360
def in_gamut(L, C, H):
    a, b = C * math.cos(math.radians(H)), C * math.sin(math.radians(H))
    return all(-1e-4 <= c <= 1 + 1e-4 for c in oklab2lin(L, a, b))
def from_oklch(L, C, H):
    """Reduce chroma until in sRGB gamut; returns (hex, C actually used)."""
    lo, hi = 0.0, C
    if in_gamut(L, C, H): lo = C
    else:
        for _ in range(24):
            mid = (lo + hi) / 2
            if in_gamut(L, mid, H): lo = mid
            else: hi = mid
    a, b = lo * math.cos(math.radians(H)), lo * math.sin(math.radians(H))
    rgb = [max(0, min(255, round(unlin(max(0, min(1, c))) * 255))) for c in oklab2lin(L, a, b)]
    return rgb2hex(rgb), lo
CVD = {  # Machado, Oliveira & Fernandes 2009, severity 1.0, linear RGB
    'protan': ((0.152286, 1.052583, -0.204868), (0.114503, 0.786281, 0.099216), (-0.003882, -0.048116, 1.051998)),
    'deutan': ((0.367322, 0.860646, -0.227968), (0.280085, 0.672501, 0.047413), (-0.011820, 0.042940, 0.968881)),
}
def cvd_lab(h, kind):
    r = [lin(c) for c in hex2rgb(h)]
    return lin2oklab(*[max(0, min(1, sum(m * v for m, v in zip(row, r)))) for row in CVD[kind]])
def dist(p, q): return 100 * math.dist(p, q)
_cache = {}
def labs(h):
    if h not in _cache: _cache[h] = (oklab(h), cvd_lab(h, 'protan'), cvd_lab(h, 'deutan'))
    return _cache[h]
def dE(a, b): return dist(labs(a)[0], labs(b)[0])
def dCvd(a, b): return min(dist(labs(a)[1], labs(b)[1]), dist(labs(a)[2], labs(b)[2]))
def lum(h): r, g, b = [lin(c) for c in hex2rgb(h)]; return 0.2126*r + 0.7152*g + 0.0722*b
def contrast(a, b): x, y = sorted((lum(a), lum(b)), reverse=True); return (x + 0.05) / (y + 0.05)

# ---------- styles ----------
# base: curated, ordered. band: OKLCH lattice the extension is drawn from.
#   L: lightness levels, C: chroma levels (clipped to gamut; dropped when below minC),
#   hues: step in degrees or explicit list; greys: extra neutral lightness levels.
# cvdWeight: weight of colour-blind distance in the farthest-point score (0 = ignore).
STYLES = json.load(open(os.path.join(HERE, 'styles.json'), encoding='utf-8'))

def candidates(style):
    """Tints and shades of the base colours: every base hue at each band lightness,
    chroma = base chroma x K (gamut-clipped). Keeps the style's hues, so its character."""
    band, out = style['band'], []
    for L in band['L']:
        for K in band.get('K', [1]):
            for b in style['base']:
                if b in style.get('noVariants', []): continue
                _, C, H = oklch(b)
                if C < 0.02 and K != band.get('K', [1])[0]: continue   # neutrals: one grey per level
                hx, _ = from_oklch(L, 0 if C < 0.02 else max(band.get('minC', 0), C * K), H)
                if hx not in out: out.append(hx)
    return out
def score(a, b, w): return min(dE(a, b), w * dCvd(a, b)) if w > 0 else dE(a, b)
def farthest(seq, pool, n, w):
    pool = [c for c in pool if c not in seq]
    while len(seq) < n and pool:
        best, bestd = None, -1
        for c in pool:                         # first index wins ties: deterministic
            d = round(min(score(c, s, w) for s in seq), 4)
            if d > bestd: best, bestd = c, d
        seq.append(best); pool.remove(best)
    return seq
def order_base(style):
    """First `lead` colours are hand-ordered (the style's signature); the rest farthest-first."""
    w, lead = style.get('cvdWeight', 1.5), style.get('lead', len(style['base']))
    return farthest(list(style['base'][:lead]), style['base'][lead:], len(style['base']), w)
def generate(style, n=CAP):
    seq = order_base(style)
    pool = style.get('extra', []) + candidates(style)
    seq = farthest(seq, pool, n, style.get('cvdWeight', 1.5))[:n]
    # Ship only the prefix that stays tell-apart-able: min pairwise dE >= MIN_DE, in steps of 4.
    k = len(seq)
    while k > len(style['base']) and (k % 4 or min(dE(seq[i], seq[j]) for i in range(k) for j in range(i)) < MIN_DE): k -= 1
    return seq[:k]
def worst(seq, k, f):
    return min(((f(seq[i], seq[j]), seq[j], seq[i]) for i in range(k) for j in range(i)))
def prefix_stats(seq):
    rows = []
    for k in range(2, len(seq) + 1):
        pairs = [(seq[i], seq[j]) for i in range(k) for j in range(i)]
        rows.append((k, min(dE(a, b) for a, b in pairs), min(dCvd(a, b) for a, b in pairs)))
    return rows

# ---------- drawing ----------
def font(size, bold=False):
    for name in (['segoeuib.ttf', 'arialbd.ttf'] if bold else ['segoeui.ttf', 'arial.ttf']):
        try: return ImageFont.truetype(name, size)
        except OSError: pass
    return ImageFont.load_default()
def shade(h, f):
    L, a, b = oklab(h); L = max(0, min(1, L + f))
    return rgb2hex([max(0, min(255, round(unlin(max(0, min(1, c))) * 255))) for c in oklab2lin(L, a, b)])
def iso_box(d, x, y, w, depth, hgt, col, line):
    """Flat-shaded axonometric box; (x, y) is the front-bottom-left corner."""
    dx, dy = depth * 0.6, depth * 0.35
    front = [(x, y), (x + w, y), (x + w, y - hgt), (x, y - hgt)]
    side = [(x + w, y), (x + w + dx, y - dy), (x + w + dx, y - dy - hgt), (x + w, y - hgt)]
    top = [(x, y - hgt), (x + w, y - hgt), (x + w + dx, y - dy - hgt), (x + dx, y - dy - hgt)]
    for poly, f in ((front, 0), (side, -0.07), (top, 0.05)):
        d.polygon(poly, fill=shade(col, f), outline=line)
def sheet(results, counts=(4, 8, 12, 16)):
    S = 2  # supersample
    sw, gap, rowh = 30 * S, 4 * S, 44 * S
    cardw = (16 * (sw // S + 4) + 150) * S
    styleh = (len(counts) * 44 + 150 + 56) * S
    W, H = cardw * 2 + 60 * S, styleh * len(results) + 60 * S
    img = Image.new('RGB', (W, H), '#E9EAEC'); d = ImageDraw.Draw(img)
    f, fb, fs = font(13 * S), font(17 * S, True), font(11 * S)
    d.text((20 * S, 14 * S), "Conduit+ diagram palette styles — first N colours on Paper and Night cards", font=fb, fill='#111111')
    y0 = 50 * S
    for name, st, seq in results:
        for side, (bg, ink, muted) in enumerate(((PAPER, '#1B1D21', '#6B7280'), (NIGHT, '#E9EAEE', '#9AA0AC'))):
            x0 = 20 * S + side * (cardw + 20 * S)
            d.rounded_rectangle((x0, y0, x0 + cardw, y0 + styleh - 14 * S), 10 * S, fill=bg)
            d.text((x0 + 16 * S, y0 + 12 * S), name, font=fb, fill=ink)
            d.text((x0 + 16 * S + d.textlength(name, font=fb) + 12 * S, y0 + 17 * S), st['look'], font=fs, fill=muted)
            y = y0 + 48 * S
            for n in counts:
                d.text((x0 + 16 * S, y + 7 * S), f"N = {n}", font=f, fill=muted)
                for i, c in enumerate(seq[:n]):
                    x = x0 + 76 * S + i * (sw + gap)
                    d.rounded_rectangle((x, y, x + sw, y + sw), 3 * S, fill=c,
                                        outline=tuple(int(v * 0.16 + w * 0.84) for v, w in zip(hex2rgb(ink), hex2rgb(c))))
                y += rowh
            # mock massing diagram: 12 programme volumes, thin line work
            line = '#1B1D21' if side == 0 else '#0E1014'
            heights = [70, 46, 96, 58, 34, 82, 50, 64, 40, 90, 54, 30]
            x = x0 + 30 * S; base = y + 118 * S
            for i, c in enumerate(seq[:12]):
                iso_box(d, x, base, 34 * S, 26 * S, heights[i] * S, c, line); x += 46 * S
        y0 += styleh
    return img.resize((W // S, H // S), Image.LANCZOS)
def html(results, counts=(4, 8, 12, 16)):
    css = """body{margin:0;padding:24px;background:#E9EAEC;font:13px/1.4 'Segoe UI',system-ui,sans-serif;color:#111}
h1{font-size:18px;margin:0 0 16px}.row{display:grid;grid-template-columns:1fr 1fr;gap:16px;margin-bottom:16px}
.card{border-radius:10px;padding:14px 16px}.paper{background:#FFF;color:#1B1D21}.night{background:#1A1D23;color:#E9EAEE}
h2{font-size:16px;margin:0 0 2px}.look{opacity:.65;font-size:12px;margin-bottom:10px}
.n{display:flex;align-items:center;gap:4px;margin:6px 0}.n span{width:52px;opacity:.6}
.s{width:30px;height:30px;border-radius:3px;box-shadow:inset 0 0 0 1px rgba(128,128,128,.28)}
.key{display:grid;grid-template-columns:repeat(4,1fr);gap:4px 12px;margin-top:10px;font-size:11px}
.key div{display:flex;align-items:center;gap:6px}.key i{width:12px;height:12px;border-radius:2px;flex:none;box-shadow:inset 0 0 0 1px rgba(128,128,128,.28)}
@media(max-width:900px){.row{grid-template-columns:1fr}}"""
    out = [f"<!doctype html><meta charset=utf-8><meta name=viewport content='width=device-width,initial-scale=1'><title>Diagram palette styles</title><style>{css}</style><h1>Conduit+ diagram palette styles</h1>"]
    for name, st, seq in results:
        out.append("<div class=row>")
        for cls in ('paper', 'night'):
            out.append(f"<div class='card {cls}'><h2>{name}</h2><div class=look>{st['look']}</div>")
            for n in counts:
                out.append(f"<div class=n><span>N = {n}</span>" + "".join(f"<div class=s style='background:{c}' title='{c}'></div>" for c in seq[:n]) + "</div>")
            out.append("<div class=key>" + "".join(f"<div><i style='background:{c}'></i>{c}</div>" for c in seq[:CAP]) + "</div></div>")
        out.append("</div>")
    return "\n".join(out)

if __name__ == '__main__':
    results, report = [], []
    for name, st in STYLES.items():
        seq = generate(st)
        results.append((name, st, seq))
        stats = prefix_stats(seq)
        get = lambda k: next(s for s in stats if s[0] == k)
        low_p = [c for c in seq[:len(st['base'])] if contrast(c, PAPER) < 1.5]
        low_n = [c for c in seq[:len(st['base'])] if contrast(c, NIGHT) < 1.5]
        report.append(f"## {name} (base {len(st['base'])})\n" + " ".join(seq) + "\n"
            + "  ".join(f"N={k}: dE {get(k)[1]:.1f} cvd {get(k)[2]:.1f}" for k in sorted({4, 6, 8, len(st['base']), 12, 16, 20, 24}) if k <= len(seq)) + f"  -> ships {len(seq)}"
            + "\nworst pairs: " + "; ".join(f"N={k} dE {worst(seq, k, dE)[1]}~{worst(seq, k, dE)[2]} cvd {worst(seq, k, dCvd)[1]}~{worst(seq, k, dCvd)[2]}" for k in (6, 8, len(st['base'])))
            + f"\n<1.5:1 on Paper: {low_p or 'none'}; on Night: {low_n or 'none'}"
            + f"\n3:1 on both: {sum(1 for c in seq[:len(st['base'])] if contrast(c, PAPER) >= 3 and contrast(c, NIGHT) >= 3)}/{len(st['base'])}\n")
    open(os.path.join(HERE, 'report.txt'), 'w', encoding='utf-8').write("\n".join(report))
    json.dump({n: s for n, _, s in results}, open(os.path.join(HERE, 'palettes.json'), 'w'), indent=1)
    sheet(results).save(os.path.join(HERE, 'swatch-sheet.png'))
    open(os.path.join(HERE, 'swatch-sheet.html'), 'w', encoding='utf-8').write(html(results))
    print("\n".join(report))
