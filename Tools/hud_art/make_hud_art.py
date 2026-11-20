"""
Generates the hand-drawn HUD art for G7l6 (sticky notes, tape, doodled heart, sweat drop, ...).

    python Tools/hud_art/make_hud_art.py

Writes PNGs into Assets/Resources/HUD/. Outlines get 3 slightly different versions (_0, _1, _2)
so the game can cycle them ("line boil") and make the doodles look alive.
Shapes that get tinted in Unity are drawn in white.
"""
import math
import os
import random
import sys

from PIL import Image, ImageDraw, ImageFilter

SS = 4  # supersampling for smooth edges
OUT = os.path.join(os.path.dirname(__file__), "..", "..", "Assets", "Resources", "HUD")
INK = (34, 28, 38, 255)
WHITE = (255, 255, 255, 255)


# ---------------------------------------------------------------- helpers

def new(w, h):
    return Image.new("RGBA", (w * SS, h * SS), (0, 0, 0, 0))


def save(img, w, h, name):
    img.resize((w, h), Image.LANCZOS).save(os.path.join(OUT, name + ".png"))


class Wobble:
    """Smooth random offset along a path (sum of a few sine waves)."""

    def __init__(self, rng, amp, waves=3):
        self.terms = [(rng.uniform(1.0, 4.0) * (i + 1), rng.uniform(0, math.tau), amp / (i + 1)) for i in range(waves)]

    def __call__(self, s):
        return sum(a * math.sin(f * s * math.tau + p) for f, p, a in self.terms)


def wobbly(points, rng, amp, closed=False):
    """Push every point along its normal by a smooth random amount."""
    wx, wy = Wobble(rng, amp), Wobble(rng, amp)
    n = len(points)
    out = []
    for i, (x, y) in enumerate(points):
        s = i / max(1, n - 1)
        out.append((x + wx(s), y + wy(s)))
    if closed:
        out.append(out[0])
    return out


def stroke(draw, pts, width, color):
    draw.line(pts, fill=color, width=int(width), joint="curve")
    r = width / 2
    for x, y in (pts[0], pts[-1]):
        draw.ellipse((x - r, y - r, x + r, y + r), fill=color)


def sketch(img, pts_fn, rng, width, color, passes=2, amp=1.0, closed=True):
    """Draw a shape outline 1-2 times with different wobble, like a quick marker sketch."""
    d = ImageDraw.Draw(img)
    for p in range(passes):
        pts = wobbly(pts_fn(), rng, amp * SS, closed)
        stroke(d, pts, width * SS * (1.0 if p == 0 else 0.6), color)


def hatch(img, polygon, spacing, width, angle_deg, alpha_base, rng):
    """Marker fill: a semi-solid base plus diagonal strokes, clipped to the polygon."""
    w, h = img.size
    mask = Image.new("L", (w, h), 0)
    ImageDraw.Draw(mask).polygon(polygon, fill=255)

    layer = Image.new("RGBA", (w, h), (255, 255, 255, alpha_base))
    d = ImageDraw.Draw(layer)
    a = math.radians(angle_deg)
    dx, dy = math.cos(a), math.sin(a)
    diag = math.hypot(w, h)
    step = spacing * SS
    k = -diag
    while k < diag * 2:
        x0, y0 = k, 0
        pts = [(x0 + dx * t * diag * 2 - dy * rng.uniform(-1, 1) * SS, y0 + dy * t * diag * 2) for t in (0, 1)]
        d.line(pts, fill=(255, 255, 255, 255), width=int(width * SS))
        k += step * rng.uniform(0.8, 1.2)

    out = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    out.paste(layer, (0, 0), mask)
    img.alpha_composite(out)


def shrink(points, cx, cy, f):
    return [(cx + (x - cx) * f, cy + (y - cy) * f) for x, y in points]


# ---------------------------------------------------------------- shapes

def heart_points(w, h, pad, n=140):
    pts = []
    for i in range(n):
        t = i / n * math.tau
        x = 16 * math.sin(t) ** 3
        y = 13 * math.cos(t) - 5 * math.cos(2 * t) - 2 * math.cos(3 * t) - math.cos(4 * t)
        pts.append((x, -y))
    xs, ys = [p[0] for p in pts], [p[1] for p in pts]
    sx = (w - 2 * pad) / (max(xs) - min(xs))
    sy = (h - 2 * pad) / (max(ys) - min(ys))
    return [((x - min(xs)) * sx + pad, (y - min(ys)) * sy + pad) for x, y in pts]


def drop_points(w, h, pad, n=140):
    # round bottom (circle) + point at the top, joined by tangent lines
    R = (w - 2 * pad) / 2
    cx, cy = w / 2, h - pad - R
    top = (w / 2, pad)
    H = cy - top[1]
    phi = math.asin(min(0.99, R / H))
    # From the right tangent point, around the bottom, to the left tangent point (math angles, y up).
    pts = [top]
    start, end = phi, -math.pi - phi
    for i in range(n + 1):
        a = start + (end - start) * (i / n)
        pts.append((cx + R * math.cos(a), cy - R * math.sin(a)))
    return pts


def rect_points(x0, y0, x1, y1, n_per_side=30):
    pts = []
    for (ax, ay), (bx, by) in [((x0, y0), (x1, y0)), ((x1, y0), (x1, y1)), ((x1, y1), (x0, y1)), ((x0, y1), (x0, y0))]:
        for i in range(n_per_side):
            t = i / n_per_side
            pts.append((ax + (bx - ax) * t, ay + (by - ay) * t))
    return pts


def rounded_rect_points(x0, y0, x1, y1, r, n=24):
    pts = []
    corners = [(x1 - r, y0 + r, -90), (x1 - r, y1 - r, 0), (x0 + r, y1 - r, 90), (x0 + r, y0 + r, 180)]
    for cx, cy, a0 in corners:
        for i in range(n + 1):
            a = math.radians(a0 + 90 * i / n)
            pts.append((cx + r * math.cos(a), cy + r * math.sin(a)))
    return pts


# ---------------------------------------------------------------- art

def make_note(rng):
    w, h = 680, 260
    img = new(w, h)
    d = ImageDraw.Draw(img)
    S = SS
    # slightly uneven paper edge
    pts = wobbly(rect_points(6 * S, 6 * S, (w - 6) * S, (h - 6) * S, 40), rng, 1.2 * S, closed=True)
    d.polygon(pts, fill=(255, 255, 255, 255))
    # sticky strip at the top is a touch darker, bottom edge curls (darker)
    shade = Image.new("RGBA", img.size, (0, 0, 0, 0))
    sd = ImageDraw.Draw(shade)
    for y in range(0, 30 * S):
        sd.line([(0, y + 6 * S), (w * S, y + 6 * S)], fill=(0, 0, 0, int(14 * (1 - y / (30 * S)))))
    for y in range(0, 40 * S):
        yy = (h - 6) * S - y
        sd.line([(0, yy), (w * S, yy)], fill=(0, 0, 0, int(22 * (1 - y / (40 * S)) ** 2)))
    mask = Image.new("L", img.size, 0)
    ImageDraw.Draw(mask).polygon(pts, fill=255)
    clipped = Image.new("RGBA", img.size, (0, 0, 0, 0))
    clipped.paste(shade, (0, 0), mask)
    img.alpha_composite(clipped)
    # paper grain
    grain = Image.effect_noise(img.size, 18).convert("L")
    grain_rgba = Image.merge("RGBA", (grain, grain, grain, Image.new("L", img.size, 10)))
    clipped = Image.new("RGBA", img.size, (0, 0, 0, 0))
    clipped.paste(grain_rgba, (0, 0), mask)
    img.alpha_composite(clipped)
    # folded corner (bottom right)
    f = 34 * S
    x1, y1 = (w - 6) * S, (h - 6) * S
    d = ImageDraw.Draw(img)
    d.polygon([(x1 - f, y1), (x1, y1 - f), (x1, y1)], fill=(0, 0, 0, 0))
    d.polygon([(x1 - f, y1), (x1, y1 - f), (x1 - f * 0.9, y1 - f * 0.9)], fill=(236, 236, 236, 255))
    d.line([(x1 - f, y1), (x1 - f * 0.9, y1 - f * 0.9), (x1, y1 - f)], fill=(0, 0, 0, 40), width=2 * S)
    save(img, w, h, "note_paper")


def make_note_shadow():
    w, h = 720, 300
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rounded_rectangle((26, 26, w - 26, h - 22), radius=10, fill=(20, 12, 8, 120))
    img = img.filter(ImageFilter.GaussianBlur(11))
    img.save(os.path.join(OUT, "note_shadow.png"))


def make_tape(rng):
    w, h = 220, 64
    img = new(w, h)
    S = SS
    top, bot = 8 * S, (h - 8) * S
    left, right = 10 * S, (w - 10) * S
    pts = []
    # torn zig-zag ends
    for i in range(9):
        t = i / 8
        pts.append((right + rng.uniform(-6, 6) * S, top + (bot - top) * t))
    for i in range(9):
        t = i / 8
        pts.append((left + rng.uniform(-6, 6) * S, bot - (bot - top) * t))
    pts = [(left, top), (right, top)] + pts[1:8] + [(right, bot), (left, bot)] + pts[10:17]
    d = ImageDraw.Draw(img)
    d.polygon(pts, fill=(246, 240, 222, 165))
    for _ in range(5):
        y = rng.uniform(top, bot)
        d.line([(left, y), (right, y + rng.uniform(-3, 3) * S)], fill=(255, 255, 255, 40), width=2 * S)
    save(img, w, h, "tape")


def make_heart(rng):
    w, h, pad = 220, 200, 16
    pts = lambda: heart_points(w * SS, h * SS, pad * SS)
    base = pts()
    fill = new(w, h)
    hatch(fill, shrink(base, w * SS / 2, h * SS / 2, 0.93), 9, 2.4, 60, 205, rng)
    save(fill, w, h, "heart_fill")
    for i in range(3):
        img = new(w, h)
        sketch(img, pts, random.Random(100 + i), 6.5, INK, passes=2, amp=2.2)
        # little shine mark
        d = ImageDraw.Draw(img)
        sx, sy = w * 0.3 * SS, h * 0.28 * SS
        stroke(d, wobbly([(sx, sy + 14 * SS), (sx + 6 * SS, sy + 2 * SS), (sx + 18 * SS, sy - 4 * SS)], random.Random(200 + i), 1.2 * SS), 5 * SS, (255, 255, 255, 230))
        save(img, w, h, f"heart_outline_{i}")


def make_drop(rng):
    w, h, pad = 160, 220, 14
    pts = lambda: drop_points(w * SS, h * SS, pad * SS)
    base = pts()
    fill = new(w, h)
    hatch(fill, shrink(base, w * SS / 2, h * SS * 0.62, 0.9), 9, 2.4, 60, 205, rng)
    save(fill, w, h, "drop_fill")
    for i in range(3):
        img = new(w, h)
        sketch(img, pts, random.Random(300 + i), 6.5, INK, passes=2, amp=2.0)
        d = ImageDraw.Draw(img)
        sx, sy = w * 0.36 * SS, h * 0.55 * SS
        stroke(d, wobbly([(sx, sy), (sx - 2 * SS, sy + 20 * SS), (sx + 6 * SS, sy + 36 * SS)], random.Random(400 + i), 1.2 * SS), 5 * SS, (255, 255, 255, 230))
        save(img, w, h, f"drop_outline_{i}")


def make_bar(rng):
    w, h = 440, 52
    S = SS
    fill = new(w, h)
    hatch(fill, rect_points(8 * S, 9 * S, (w - 8) * S, (h - 9) * S, 4), 8, 3.0, 65, 150, rng)
    save(fill, w, h, "bar_fill")
    for i in range(3):
        img = new(w, h)
        sketch(img, lambda: rect_points(6 * S, 7 * S, (w - 6) * S, (h - 7) * S, 30), random.Random(500 + i), 3.6, INK, passes=2, amp=1.3)
        save(img, w, h, f"bar_outline_{i}")


def make_keycap(rng):
    w, h = 112, 112
    S = SS
    for i in range(3):
        r = random.Random(600 + i)
        img = new(w, h)
        d = ImageDraw.Draw(img)
        side = wobbly(rounded_rect_points(10 * S, 16 * S, (w - 10) * S, (h - 8) * S, 18 * S), r, 1.0 * S, True)
        d.polygon(side, fill=(176, 170, 182, 255))
        top = wobbly(rounded_rect_points(10 * S, 8 * S, (w - 10) * S, (h - 20) * S, 18 * S), r, 1.0 * S, True)
        d.polygon(top, fill=(252, 250, 244, 255))
        sketch(img, lambda: rounded_rect_points(10 * S, 8 * S, (w - 10) * S, (h - 8) * S, 18 * S), r, 4.5, INK, passes=1, amp=1.4)
        stroke(d, wobbly([(14 * S, (h - 20) * S), ((w - 14) * S, (h - 20) * S)], r, 1.0 * S), 3 * S, (34, 28, 38, 160))
        save(img, w, h, f"keycap_{i}")


def make_strike(rng):
    w, h = 560, 44
    img = new(w, h)
    d = ImageDraw.Draw(img)
    pts = [(12 * SS + (w - 24) * SS * t, (h / 2 + 6 - 12 * t) * SS) for t in [i / 40 for i in range(41)]]
    stroke(d, wobbly(pts, rng, 2.2 * SS), 9 * SS, WHITE)
    save(img, w, h, "strike")


def make_check(rng):
    w, h = 120, 120
    for i in range(3):
        img = new(w, h)
        d = ImageDraw.Draw(img)
        S = SS
        a, b, c = (18 * S, 62 * S), (48 * S, 96 * S), (104 * S, 18 * S)
        pts = [a] + [(a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t) for t in [k / 12 for k in range(1, 13)]] + \
              [(b[0] + (c[0] - b[0]) * t, b[1] + (c[1] - b[1]) * t) for t in [k / 20 for k in range(1, 21)]]
        stroke(d, wobbly(pts, random.Random(700 + i), 1.6 * S), 12 * S, WHITE)
        save(img, w, h, f"check_{i}")


def make_circle(rng):
    w, h = 170, 110
    for i in range(3):
        r = random.Random(800 + i)
        img = new(w, h)
        d = ImageDraw.Draw(img)
        cx, cy = w / 2 * SS, h / 2 * SS
        pts = []
        turns = 1.12
        for k in range(160):
            t = k / 159
            a = -2.2 + t * turns * math.tau
            grow = 1 + 0.06 * t
            pts.append((cx + math.cos(a) * (w / 2 - 10) * SS * grow * 0.95, cy + math.sin(a) * (h / 2 - 10) * SS * (0.92 + 0.08 * t)))
        stroke(d, wobbly(pts, r, 1.6 * SS), 6 * SS, WHITE)
        save(img, w, h, f"circle_{i}")


def make_underline(rng):
    w, h = 360, 40
    for i in range(3):
        img = new(w, h)
        d = ImageDraw.Draw(img)
        pts = [(10 * SS + (w - 20) * SS * t, (h * 0.55 - 8 * math.sin(t * math.pi) + 6 * t) * SS) for t in [k / 50 for k in range(51)]]
        stroke(d, wobbly(pts, random.Random(900 + i), 1.6 * SS), 6 * SS, WHITE)
        save(img, w, h, f"underline_{i}")


def make_tag(rng):
    w, h = 520, 130
    S = SS
    img = new(w, h)
    d = ImageDraw.Draw(img)
    x0, y0, x1, y1, cut = 40 * S, 16 * S, (w - 10) * S, (h - 16) * S, 34 * S
    shape = [(x0 + cut, y0), (x1, y0), (x1, y1), (x0 + cut, y1), (x0, y1 - cut), (x0, y0 + cut)]
    dense = []
    for (ax, ay), (bx, by) in zip(shape, shape[1:] + shape[:1]):
        for k in range(20):
            t = k / 20
            dense.append((ax + (bx - ax) * t, ay + (by - ay) * t))
    d.polygon(dense, fill=WHITE)
    grain = Image.effect_noise(img.size, 16).convert("L")
    mask = Image.new("L", img.size, 0)
    ImageDraw.Draw(mask).polygon(dense, fill=255)
    g = Image.merge("RGBA", (grain, grain, grain, Image.new("L", img.size, 12)))
    clipped = Image.new("RGBA", img.size, (0, 0, 0, 0))
    clipped.paste(g, (0, 0), mask)
    img.alpha_composite(clipped)
    sketch(img, lambda: dense, rng, 3.5, INK, passes=1, amp=1.0)
    d = ImageDraw.Draw(img)
    hx, hy, hr = x0 + 30 * S, h / 2 * S, 9 * S
    d.ellipse((hx - hr, hy - hr, hx + hr, hy + hr), fill=(0, 0, 0, 0), outline=INK, width=3 * S)
    # string
    pts = [(hx, hy)] + [(hx - 34 * S * t, hy - (h / 2 - 4) * S * t + 10 * S * math.sin(t * math.pi)) for t in [k / 20 for k in range(1, 21)]]
    stroke(d, wobbly(pts, rng, 1.0 * S), 3 * S, (120, 90, 60, 255))
    save(img, w, h, "tag")


def make_scrap(rng):
    w, h = 1000, 150
    S = SS
    img = new(w, h)
    top, bot = 22 * S, (h - 22) * S
    pts = []
    n = 70
    for i in range(n + 1):
        x = 14 * S + (w - 28) * S * i / n
        pts.append((x, top + rng.uniform(-7, 7) * S))
    for i in range(n + 1):
        x = (w - 14) * S - (w - 28) * S * i / n
        pts.append((x, bot + rng.uniform(-7, 7) * S))
    shadow = Image.new("RGBA", img.size, (0, 0, 0, 0))
    ImageDraw.Draw(shadow).polygon([(x + 6 * S, y + 9 * S) for x, y in pts], fill=(0, 0, 0, 90))
    shadow = shadow.filter(ImageFilter.GaussianBlur(8 * S))
    img.alpha_composite(shadow)
    ImageDraw.Draw(img).polygon(pts, fill=WHITE)
    grain = Image.effect_noise(img.size, 16).convert("L")
    mask = Image.new("L", img.size, 0)
    ImageDraw.Draw(mask).polygon(pts, fill=255)
    g = Image.merge("RGBA", (grain, grain, grain, Image.new("L", img.size, 12)))
    clipped = Image.new("RGBA", img.size, (0, 0, 0, 0))
    clipped.paste(g, (0, 0), mask)
    img.alpha_composite(clipped)
    save(img, w, h, "scrap")


def contact_sheet(path):
    names = sorted(f for f in os.listdir(OUT) if f.endswith(".png"))
    tiles = [Image.open(os.path.join(OUT, n)).convert("RGBA") for n in names]
    W = 1400
    x = y = 0
    row_h = 0
    placed = []
    for n, t in zip(names, tiles):
        t.thumbnail((330, 240))
        if x + t.width > W:
            x, y, row_h = 0, y + row_h + 10, 0
        placed.append((t, x, y))
        x += t.width + 10
        row_h = max(row_h, t.height)
    sheet = Image.new("RGBA", (W, y + row_h + 10), (120, 140, 150, 255))
    for t, px, py in placed:
        sheet.alpha_composite(t, (px, py))
    sheet.convert("RGB").save(path)


if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True)
    rng = random.Random(7)
    make_note(rng)
    make_note_shadow()
    make_tape(rng)
    make_heart(rng)
    make_drop(rng)
    make_bar(rng)
    make_keycap(rng)
    make_strike(rng)
    make_check(rng)
    make_circle(rng)
    make_underline(rng)
    make_tag(rng)
    make_scrap(rng)
    if len(sys.argv) > 1:
        contact_sheet(sys.argv[1])
    print("HUD art written to", os.path.abspath(OUT))
