"""Builds Book Hand, Enhanced mode's book font, from the stroke drawings in glyphs.py.

Run from bakagain/:
    uv run --with fonttools --with shapely python tools/bookhand/build.py unity/Assets/BuiltIn/Fonts/BookHand/BookHand-Regular.ttf
    uv run --with fonttools --with shapely python tools/bookhand/build.py --check-only <font.ttf>
"""
import math
import sys

UX = 48  # font units per design column; the em is 16 columns (FntTrueType: BOOK.FNT's Height + 1)
UPM = 16 * UX
UY = UX * 96 / 70  # a row: BOOK.FNT's EGA pixels are 96/35 tall for 2 wide
ASCENT, DESCENT = 11, 4  # rows, as FntTrueType writes them for BOOK.FNT
NIB_W, NIB_ANGLE = 112, math.radians(32)  # the pen: its width in font units and the angle it is held at


def catmull(pts, n=14):
    """A smooth path through the stroke's points (Catmull-Rom); two points make a straight line."""
    if len(pts) < 3:
        (x0, y0), (x1, y1) = pts
        return [(x0 + (x1 - x0) * t / n, y0 + (y1 - y0) * t / n) for t in range(n + 1)]
    p = [pts[0]] + pts + [pts[-1]]
    out = []
    for i in range(1, len(p) - 2):
        p0, p1, p2, p3 = p[i - 1], p[i], p[i + 1], p[i + 2]
        for k in range(n):
            t = k / n
            out.append(tuple(0.5 * (2 * p1[j] + (-p0[j] + p2[j]) * t + (2 * p0[j] - 5 * p1[j] + 4 * p2[j] - p3[j]) * t * t
                                    + (-p0[j] + 3 * p1[j] - 3 * p2[j] + p3[j]) * t ** 3) for j in (0, 1)))
    return out + [pts[-1]]


def sweep(stroke, taper=(1.0, 1.0)):
    """The shape a broad nib leaves when its centre follows the stroke: the union of the quads
    between consecutive nib positions. taper scales the nib from the stroke's start to its end."""
    from shapely.geometry import Polygon
    from shapely.ops import unary_union
    path = catmull([(x * UX, y * UY) for x, y in stroke])
    n = len(path)
    nib = []
    for i, (x, y) in enumerate(path):
        half = (taper[0] + (taper[1] - taper[0]) * i / (n - 1)) * NIB_W / 2
        dx, dy = math.cos(NIB_ANGLE) * half, math.sin(NIB_ANGLE) * half
        nib.append(((x - dx, y - dy), (x + dx, y + dy)))
    quads = [Polygon([nib[i][0], nib[i][1], nib[i + 1][1], nib[i + 1][0]]).buffer(0).convex_hull for i in range(n - 1)]
    return unary_union([q for q in quads if q.area > 0])


def dot(x, y, size):
    """A dot: a short stroke across the nib, which leaves a lozenge. size scales the pen too, so the
    two dots of an umlaut stay apart."""
    return sweep([(x + 0.38 * size, y - 0.45 * size), (x - 0.38 * size, y + 0.45 * size)], (size, size))


def outline(ch):
    from shapely.ops import unary_union
    from glyphs import STROKES, DOTS, TAPER
    parts = [sweep(s, TAPER.get(ch, (1.0, 1.0))) for s in STROKES[ch]]
    parts += [dot(*d) for d in DOTS.get(ch, [])]
    return unary_union(parts) if parts else None


def glyph_name(ch):
    return 'space' if ch == ' ' else f'uni{ord(ch):04X}'


def build(out_path):
    from fontTools.fontBuilder import FontBuilder
    from fontTools.pens.ttGlyphPen import TTGlyphPen
    from shapely.geometry.polygon import orient
    from glyphs import ADVANCES
    order = ['.notdef'] + [glyph_name(c) for c in ADVANCES]
    fb = FontBuilder(UPM, isTTF=True)
    fb.setupGlyphOrder(order)
    fb.setupCharacterMap({ord(c): glyph_name(c) for c in ADVANCES})
    glyphs, metrics = {'.notdef': TTGlyphPen(None).glyph()}, {'.notdef': (UPM // 2, 0)}
    for ch, cols in ADVANCES.items():
        pen = TTGlyphPen(None)
        geom = outline(ch)
        if geom is not None and not geom.is_empty:
            for poly in ([geom] if geom.geom_type == 'Polygon' else list(geom.geoms)):
                poly = orient(poly.simplify(1.5), sign=-1.0)  # TrueType: outer contours clockwise
                for ring in [poly.exterior] + list(poly.interiors):
                    pts = [(round(x), round(y)) for x, y in list(ring.coords)[:-1]]
                    pen.moveTo(pts[0])
                    for q in pts[1:]:
                        pen.lineTo(q)
                    pen.closePath()
        glyphs[glyph_name(ch)] = pen.glyph()
        metrics[glyph_name(ch)] = (cols * UX, 0)
    fb.setupGlyf(glyphs)
    for name, (adv, _) in metrics.items():  # left side bearing = the outline's xMin
        metrics[name] = (adv, getattr(glyphs[name], 'xMin', 0))
    fb.setupHorizontalMetrics(metrics)
    asc, desc = round(ASCENT * UY), -round(DESCENT * UY)
    fb.setupHorizontalHeader(ascent=asc, descent=desc, lineGap=0)
    fb.setupNameTable({'familyName': 'Book Hand', 'styleName': 'Regular',
                       'uniqueFontIdentifier': 'BaKAgain: Book Hand Regular: 2026',
                       'fullName': 'Book Hand Regular', 'psName': 'BookHand-Regular',
                       'copyright': 'Copyright 2026 BaK Again contributors',
                       'licenseDescription': 'This Font Software is licensed under the SIL Open Font License, Version 1.1.',
                       'licenseInfoURL': 'https://openfontlicense.org'})
    fb.setupOS2(sTypoAscender=asc, sTypoDescender=desc, sTypoLineGap=0, usWinAscent=asc, usWinDescent=-desc,
                sxHeight=round(7 * UY), sCapHeight=asc, fsType=0)
    fb.setupPost()
    fb.save(out_path)


def check(font_path, advances):
    """The font's contract with the book view: every character, each at BOOK.FNT's advance, no kerning."""
    from fontTools.ttLib import TTFont
    f = TTFont(font_path); cmap = f.getBestCmap(); hmtx = f['hmtx']; glyf = f['glyf']
    assert f['head'].unitsPerEm == UPM, f"unitsPerEm {f['head'].unitsPerEm} != {UPM}"
    for ch, cols in advances.items():
        name = cmap.get(ord(ch)); assert name, f'no glyph for {ch!r}'
        assert hmtx[name][0] == cols * UX, f'{ch!r}: advance {hmtx[name][0]} != {cols * UX}'
        if not ch.isspace() and ch != '~':  # BOOK.FNT's ~ slot is empty
            assert glyf[name].numberOfContours > 0, f'{ch!r} has no outline'
    assert 'kern' not in f and 'GPOS' not in f, 'kerning would move lines off the wrap'
    names = {r.nameID for r in f['name'].names}
    assert {1, 2, 3, 4, 6} <= names, f'name table lacks IDs {sorted({1, 2, 3, 4, 6} - names)} (installers want them)'
    print(f'check: {len(advances)} glyphs OK')


if __name__ == '__main__':
    from glyphs import ADVANCES
    if sys.argv[1] == '--check-only':
        check(sys.argv[2], ADVANCES)
    else:
        build(sys.argv[1])
        check(sys.argv[1], ADVANCES)
