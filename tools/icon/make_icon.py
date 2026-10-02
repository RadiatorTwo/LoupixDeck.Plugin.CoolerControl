#!/usr/bin/env python3
"""LoupixDeck CoolerControl plugin icon (fan rotor in a level ring, matte, night blue).

Same look as the Audio plugin icon: background, ring, colors and shading are shared;
the knob is replaced by a seven-blade fan rotor.

Requires: pip install pillow numpy
Usage:    python make_icon.py [output_dir]
Writes icon_{256,128,64,32,16}.png (RGBA, transparent corners).
"""
import math
import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

SIZE = 256   # design size (px)
SS = 4       # supersampling
N = SIZE * SS
YY, XX = np.mgrid[0:N, 0:N].astype(np.float32)
XX = (XX + 0.5) / SS
YY = (YY + 0.5) / SS


def oklch(L, C, h, a=1.0):
    hr = math.radians(h)
    A, B = C * math.cos(hr), C * math.sin(hr)
    l = (L + 0.3963377774 * A + 0.2158037573 * B) ** 3
    m = (L - 0.1055613458 * A - 0.0638541728 * B) ** 3
    s = (L - 0.0894841775 * A - 1.2914855480 * B) ** 3
    lin = [4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s,
           -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s,
           -0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s]
    out = [12.92 * c if c <= 0.0031308 else 1.055 * max(c, 0) ** (1 / 2.4) - 0.055 for c in lin]
    return (*[min(max(c, 0.0), 1.0) for c in out], a)


# Colors (same as the Audio icon)
BG = oklch(0.24, 0.02, 260)
EDGE = oklch(0.32, 0.02, 260)
ARC = oklch(0.80, 0.13, 200)
TRACK = oklch(0.34, 0.02, 260)
ACCENT = oklch(0.80, 0.13, 200)

# ---------- Masks ----------
def _mask(draw_fn):
    im = Image.new("L", (N, N), 0)
    draw_fn(ImageDraw.Draw(im))
    return np.asarray(im, dtype=np.float32) / 255.0


def circle(cx, cy, r):
    return _mask(lambda d: d.ellipse([(cx - r) * SS, (cy - r) * SS, (cx + r) * SS - 1, (cy + r) * SS - 1], fill=255))


def rrect(x, y, w, h, r):
    return _mask(lambda d: d.rounded_rectangle([x * SS, y * SS, (x + w) * SS - 1, (y + h) * SS - 1], radius=r * SS, fill=255))


def polygon(points):
    return _mask(lambda d: d.polygon([(x * SS, y * SS) for x, y in points], fill=255))


def blur(mask, px):
    if px <= 0:
        return mask
    im = Image.fromarray((np.clip(mask, 0, 1) * 255).astype(np.uint8))
    im = im.filter(ImageFilter.GaussianBlur(px / 2 * SS))  # CSS blur = 2*sigma
    return np.asarray(im, dtype=np.float32) / 255.0


def shift(mask, dx, dy, fill=0.0):
    out = np.full_like(mask, fill)
    sx, sy = int(round(dx * SS)), int(round(dy * SS))
    h, w = mask.shape
    out[max(sy, 0):h + min(sy, 0), max(sx, 0):w + min(sx, 0)] = mask[max(-sy, 0):h + min(-sy, 0), max(-sx, 0):w + min(-sx, 0)]
    return out


# ---------- Compositing ----------
canvas = np.zeros((N, N, 4), dtype=np.float32)  # straight RGBA


def paint(color, alpha):
    """color: RGBA tuple or HxWx3 array; alpha: HxW mask (multiplied by the color's alpha)."""
    global canvas
    if isinstance(color, tuple):
        rgb = np.array(color[:3], dtype=np.float32)[None, None, :]
        a = alpha * color[3]
    else:
        rgb, a = color, alpha
    a = a[..., None]
    ca = canvas[..., 3:4]
    oa = a + ca * (1 - a)
    orgb = (rgb * a + canvas[..., :3] * ca * (1 - a)) / np.maximum(oa, 1e-6)
    canvas = np.concatenate([orgb, oa], axis=-1)


def drop_shadow(shape, dx, dy, blur_px, color, clip):
    paint(color, blur(shift(shape, dx, dy), blur_px) * clip)


def inset_shadow(shape, dx, dy, blur_px, color):
    paint(color, blur(shift(1 - shape, dx, dy, fill=1.0), blur_px) * shape)


def linear_gradient(box, css_deg, stops):
    x, y, w, h = box
    th = math.radians(css_deg)
    dx, dy = math.sin(th), -math.cos(th)
    L = abs(w * dx) + abs(h * dy)
    t = ((XX - (x + w / 2)) * dx + (YY - (y + h / 2)) * dy) / L + 0.5
    t = np.clip(t, 0, 1)
    pos = [s[0] for s in stops]
    return np.stack([np.interp(t, pos, [s[1][i] for s in stops]) for i in range(3)], axis=-1).astype(np.float32)


# ---------- Fan blade ----------
C = 128           # rotor center
R_ROOT = 18       # blade root radius (hidden under the hub)
R_TIP = 64        # blade tip radius
BLADES = 7


def blade(start_deg):
    """One swept blade: the leading and trailing edges bend clockwise towards the tip,
    the blade widens from root to tip and ends in a round arc."""
    steps = 24
    lead, trail = [], []
    for i in range(steps + 1):
        t = i / steps
        r = R_ROOT + (R_TIP - R_ROOT) * t
        sweep = 38 * t ** 1.3          # clockwise bend along the blade
        width = 20 + 18 * t            # angular width in degrees
        a_lead = math.radians(start_deg + sweep + width)
        a_trail = math.radians(start_deg + sweep)
        lead.append((C + r * math.sin(a_lead), C - r * math.cos(a_lead)))
        trail.append((C + r * math.sin(a_trail), C - r * math.cos(a_trail)))
    # Tip: arc on the tip circle from the leading to the trailing edge
    a0 = start_deg + 38 + 38
    a1 = start_deg + 38
    tip = [(C + R_TIP * math.sin(math.radians(a0 + (a1 - a0) * k / 8)),
            C - R_TIP * math.cos(math.radians(a0 + (a1 - a0) * k / 8))) for k in range(1, 8)]
    return polygon(lead + tip + trail[::-1])


# ---------- Draw ----------
icon = rrect(0, 0, SIZE, SIZE, 58)

# Background + 1px inner edge
paint(BG, icon)
paint(EDGE, icon - rrect(1, 1, SIZE - 2, SIZE - 2, 57))

# Level ring: conic arc from 225°, 190° long, track up to 270°, the rest empty
ang = (np.degrees(np.arctan2(XX - 128, -(YY - 128))) % 360 - 225) % 360
ring = circle(128, 128, 92)
paint(ARC, ring * (ang < 190))
paint(TRACK, ring * ((ang >= 190) & (ang < 270)))
paint(BG, circle(128, 128, 74))  # cut out the ring

# Rotor blades (plastic, matte)
blades = np.zeros((N, N), dtype=np.float32)
for k in range(BLADES):
    blades = np.maximum(blades, blade(k * 360 / BLADES))
drop_shadow(blades, 0, 14, 20, oklch(0.04, 0.04, 260, 0.80), icon)
drop_shadow(blades, 0, 4, 3, oklch(0.06, 0.03, 260, 0.55), icon)
BOX = (C - R_TIP, C - R_TIP, 2 * R_TIP, 2 * R_TIP)
paint(linear_gradient(BOX, 160, [(0, oklch(0.93, 0.006, 260)[:3]), (1, oklch(0.76, 0.01, 260)[:3])]), blades)
inset_shadow(blades, 0, -3, 4, oklch(0.4, 0.02, 260, 0.35))
inset_shadow(blades, 0, 2, 2, (1, 1, 1, 0.45))

# Hub (like the Audio knob's cap)
HR = 24
hub = circle(C, C, HR)
drop_shadow(hub, 0, 3, 6, oklch(0.1, 0.03, 260, 0.55), icon)
HB = (C - HR, C - HR, 2 * HR, 2 * HR)
paint(linear_gradient(HB, 165, [(0, oklch(0.90, 0.006, 260)[:3]), (1, oklch(0.78, 0.008, 260)[:3])]), hub)
# Highlight: radial at 36%/26%, 35% white fading to 0 at 55% of the farthest-corner radius
hx, hy = C - HR + 2 * HR * 0.36, C - HR + 2 * HR * 0.26
far = max(math.hypot(hx - cx, hy - cy) for cx in (C - HR, C + HR) for cy in (C - HR, C + HR))
t = np.clip(np.hypot(XX - hx, YY - hy) / (far * 0.55), 0, 1)
paint((1, 1, 1, 1.0), hub * (0.35 * (1 - t)))
inset_shadow(hub, 0, -3, 5, oklch(0.4, 0.02, 260, 0.30))
inset_shadow(hub, 0, 2, 3, (1, 1, 1, 0.50))

# Accent dot in the hub, in the ring's color
paint(ACCENT, circle(C, C, 7))
inset_shadow(circle(C, C, 7), 0, 1.5, 2, oklch(0.3, 0.05, 220, 0.45))

# Clip to the icon shape
canvas[..., 3] *= icon

# ---------- Export ----------
if __name__ == "__main__":
    out_dir = sys.argv[1] if len(sys.argv) > 1 else "."
    os.makedirs(out_dir, exist_ok=True)
    big = Image.fromarray((np.clip(canvas, 0, 1) * 255 + 0.5).astype(np.uint8), "RGBA")
    for s in (256, 128, 64, 32, 16):
        path = os.path.join(out_dir, f"icon_{s}.png")
        big.resize((s, s), Image.LANCZOS).save(path)
        print("written:", path)
