# The Enhanced sword cursor (TASK-972, owner 2026-11-01): vector shapes, supersampled and scaled down.
# Laid out after the original pointer (POINTER.BMX #0): the tip at the top-left is the hotspot, and
# blade, guard, grip and pommel stand where its pixels do, at 3x its 80x54 size. No pixel is copied.
# Usage: python3 tools/sword-cursor/sword.py unity/Assets/BuiltIn/Enhanced/Stone/sword-cursor.png
import sys, math
from PIL import Image, ImageDraw, ImageFilter
out = sys.argv[1]
W, H = 240, 162            # 3x the original's 80x54
S = 8                      # supersampling
U = (W * S / 16.0, H * S / 9.0)    # one original pixel, in supersampled px (the sprite is 16x9, 5x6 units each)
def P(x, y): return (x * U[0], y * U[1])
img = Image.new('RGBA', (W * S, H * S), (0, 0, 0, 0))
d = ImageDraw.Draw(img)
gold_dk, gold, cream = (150, 120, 48, 255), (196, 160, 70, 255), (236, 218, 150, 255)
steel_dk, steel, steel_lt = (58, 110, 165, 255), (182, 150, 70, 255), (238, 222, 156, 255)
grip_dk, grip = (70, 24, 20, 255), (140, 72, 42, 255)
outline = (20, 12, 6, 230)
r2 = math.sqrt(0.5)
along, perp = (r2, r2), (r2, -r2)
def pt(t, s):            # t along the sword from the tip, s across (+ = up-right)
    return P(t * along[0] + s * perp[0], t * along[1] + s * perp[1])
def poly(points, fill, w=0):
    d.polygon(points, fill=fill, outline=outline if w else None, width=w)
# Blade: tip at t=0.15, base at t=6.6, half width 0.62 at the base.
tip, base, hw = 0.15, 6.6, 0.78
def blade(scale, fill):
    poly([pt(tip, 0), pt(base, hw * scale), pt(base, -hw * scale)], fill)
d.polygon([pt(tip - 0.25, 0), pt(base + 0.2, hw + 0.25), pt(base + 0.2, -hw - 0.25)], fill=outline)
poly([pt(tip, 0), pt(base, hw), pt(base, 0)], steel_lt)          # the lit edge
poly([pt(tip, 0), pt(base, 0), pt(base, -hw)], steel)            # the shaded edge
poly([pt(tip + 0.3, -0.02), pt(base, -hw * 0.45), pt(base, -hw)], steel_dk)   # the original's blue edge
d.line([pt(tip + 0.9, 0), pt(base, 0)], fill=(150, 120, 48, 255), width=int(U[0] * 0.10))   # fuller
# Crossguard across the blade's base.
g0, g1, gl, gw = base, base + 0.55, 2.3, 0
d.polygon([pt(g0 - 0.15, gl + 0.2), pt(g1 + 0.15, gl + 0.2), pt(g1 + 0.15, -gl - 0.2), pt(g0 - 0.15, -gl - 0.2)], fill=outline)
poly([pt(g0, gl), pt(g1, gl), pt(g1, -gl), pt(g0, -gl)], gold)
poly([pt(g0, gl), pt(g0 + 0.22, gl), pt(g0 + 0.22, -gl), pt(g0, -gl)], cream)
for s in (gl, -gl):
    c = pt((g0 + g1) / 2, s); r = U[0] * 0.42
    d.ellipse([c[0] - r - U[0] * 0.12, c[1] - r - U[0] * 0.12, c[0] + r + U[0] * 0.12, c[1] + r + U[0] * 0.12], fill=outline)
    d.ellipse([c[0] - r, c[1] - r, c[0] + r, c[1] + r], fill=gold)
# Grip, wrapped.
h0, h1, hh = g1, g1 + 2.6, 0.42
d.polygon([pt(h0, hh + 0.15), pt(h1, hh + 0.15), pt(h1, -hh - 0.15), pt(h0, -hh - 0.15)], fill=outline)
poly([pt(h0, hh), pt(h1, hh), pt(h1, -hh), pt(h0, -hh)], grip_dk)
k = h0 + 0.25
while k < h1 - 0.2:
    poly([pt(k, hh), pt(k + 0.22, hh), pt(k + 0.42, -hh), pt(k + 0.2, -hh)], grip)
    k += 0.55
# Pommel.
c = pt(h1 + 0.55, 0); r = U[0] * 0.72
d.ellipse([c[0] - r - U[0] * 0.15, c[1] - r - U[0] * 0.15, c[0] + r + U[0] * 0.15, c[1] + r + U[0] * 0.15], fill=outline)
d.ellipse([c[0] - r, c[1] - r, c[0] + r, c[1] + r], fill=gold_dk)
d.ellipse([c[0] - r * 0.7, c[1] - r * 0.75, c[0] + r * 0.45, c[1] + r * 0.4], fill=gold)
d.ellipse([c[0] - r * 0.45, c[1] - r * 0.5, c[0] - r * 0.05, c[1] - r * 0.1], fill=cream)
img = img.resize((W, H), Image.LANCZOS)
img.save(out)
