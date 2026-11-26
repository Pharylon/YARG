#!/usr/bin/env python3
"""Generate the nine gig-map badge images for the "Rock Band 1" career setlist.

Each badge is a 256x256 RGBA PNG: a dark navy background filling the square (the game crops it to
the inscribed circle) with one bold flat subject inside the central ~70%. Everything is drawn from
plain shapes at 4x (1024 px) and downsampled with LANCZOS.

Usage: python3 make_rock_band_1_art.py [output_dir]
(default output_dir: "Rock Band 1" next to this script)
"""

import math
import os
import sys

from PIL import Image, ImageChops, ImageDraw, ImageFilter

S = 1024  # working size
OUT_SIZE = 256
C = S // 2

NAVY_IN = (11, 42, 74)
NAVY_OUT = (6, 24, 44)
RING = (24, 66, 104)
INK = (3, 12, 26)  # outline colour
CYAN = (46, 217, 255)
CYAN_DK = (18, 130, 180)
CYAN_LT = (150, 236, 255)
WHITE = (255, 255, 255)
PALE = (205, 225, 240)
AMBER = (255, 194, 51)
AMBER_DK = (214, 140, 20)
PINK = (255, 61, 154)
PINK_DK = (190, 30, 110)
ORANGE = (255, 128, 40)
ORANGE_DK = (196, 84, 22)
ORANGE_LT = (255, 178, 110)
FRETBOARD = (16, 52, 86)

OW = 16  # inner outline width (working px)


# ----------------------------------------------------------------------------- helpers

_BG = None


def background():
    global _BG
    if _BG is None:
        _BG = _make_background()
    return _BG.copy()


def _make_background():
    img = Image.new("RGBA", (S, S))
    px = img.load()
    for y in range(S):
        for x in range(S):
            d = math.hypot(x - C, y - C * 0.85) / (S * 0.72)
            t = min(1.0, d)
            t = t * t * (3 - 2 * t)
            px[x, y] = tuple(int(NAVY_IN[i] + (NAVY_OUT[i] - NAVY_IN[i]) * t) for i in range(3)) + (255,)
    d = ImageDraw.Draw(img)
    r = 452
    d.ellipse([C - r, C - r, C + r, C + r], outline=RING, width=10)
    return img


def poly(d, pts, fill, w=OW, outline=INK):
    d.polygon(pts, fill=fill)
    if w:
        d.line(list(pts) + [pts[0]], fill=outline, width=w, joint="curve")
        for p in pts:
            dot(d, p, w / 2, outline)


def dot(d, p, r, fill):
    d.ellipse([p[0] - r, p[1] - r, p[0] + r, p[1] + r], fill=fill)


def circle(d, cx, cy, r, fill, w=OW, outline=INK):
    d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=fill, outline=outline if w else None, width=w)


def rect(d, x0, y0, x1, y1, fill, w=OW, outline=INK, radius=0):
    if radius:
        d.rounded_rectangle([x0, y0, x1, y1], radius=radius, fill=fill, outline=outline if w else None, width=w)
    else:
        d.rectangle([x0, y0, x1, y1], fill=fill, outline=outline if w else None, width=w)


def stroke(d, pts, color, width, outline=INK, ow=OW):
    """A line drawn with a dark casing around it, round caps."""
    if ow:
        d.line(pts, fill=outline, width=width + ow * 2, joint="curve")
        for p in (pts[0], pts[-1]):
            dot(d, p, (width + ow * 2) / 2, outline)
    d.line(pts, fill=color, width=width, joint="curve")
    for p in (pts[0], pts[-1]):
        dot(d, p, width / 2, color)


def wave(x0, x1, y, amp, period, steps=120):
    pts = []
    for i in range(steps + 1):
        x = x0 + (x1 - x0) * i / steps
        pts.append((x, y + amp * math.sin((x - x0) / period * 2 * math.pi)))
    return pts


def arc_pts(cx, cy, rx, ry, a0, a1, steps=80):
    return [(cx + rx * math.cos(math.radians(a0 + (a1 - a0) * i / steps)),
             cy + ry * math.sin(math.radians(a0 + (a1 - a0) * i / steps))) for i in range(steps + 1)]


def compose(subject, glow=CYAN, light=None):
    """Background + optional light layer (no outline) + soft glow + thick silhouette outline + subject."""
    bg = background()
    if light is not None:
        bg = Image.alpha_composite(bg, light)
    alpha = subject.getchannel("A")

    g = alpha.filter(ImageFilter.GaussianBlur(40)).point(lambda v: int(v * 0.35))
    glow_layer = Image.new("RGBA", (S, S), glow + (0,))
    glow_layer.putalpha(g)
    bg = Image.alpha_composite(bg, glow_layer)

    # Silhouette outline: dilate the alpha by blurring and thresholding.
    o = alpha.filter(ImageFilter.GaussianBlur(9)).point(lambda v: 0 if v < 6 else (255 if v > 30 else int((v - 6) * 255 / 24)))
    o = ImageChops.lighter(o, alpha)
    out_layer = Image.new("RGBA", (S, S), INK + (0,))
    out_layer.putalpha(o)
    bg = Image.alpha_composite(bg, out_layer)

    bg = Image.alpha_composite(bg, subject)
    return bg.resize((OUT_SIZE, OUT_SIZE), Image.LANCZOS)


def layer():
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    return img, ImageDraw.Draw(img)


# ----------------------------------------------------------------------------- badges

def badge_broken_string():
    """1. The Broken String: guitar headstock with one snapped, curling string."""
    img, d = layer()
    cx = 450
    top, nut, bot = 200, 480, 900
    # neck / fretboard (drawn upright, tilted afterwards)
    rect(d, cx - 78, nut, cx + 78, bot, FRETBOARD)
    for y in (620, 770):  # frets
        d.line([(cx - 70, y), (cx + 70, y)], fill=PALE, width=14)
    # tuning pegs (behind the headstock)
    pegs = [(-1, 270), (-1, 390), (1, 270), (1, 390)]
    for side, y in pegs:
        stroke(d, [(cx, y), (cx + side * 175, y)], WHITE, 30, ow=14)
        circle(d, cx + side * 190, y, 46, AMBER, w=OW)
    # headstock
    head = [(cx - 84, nut), (cx - 140, nut - 110), (cx - 150, top + 60), (cx - 80, top),
            (cx + 80, top), (cx + 150, top + 60), (cx + 140, nut - 110), (cx + 84, nut)]
    poly(d, head, CYAN)
    rect(d, cx - 88, nut - 18, cx + 88, nut + 20, WHITE)  # nut
    for side, y in pegs:  # string posts
        circle(d, cx + side * 80, y, 22, WHITE, w=10)
    # two intact strings
    for sx, p in ((cx - 40, (cx - 80, 270)), (cx + 40, (cx + 80, 270))):
        d.line([p, (sx, nut), (sx, bot)], fill=WHITE, width=16, joint="curve")
    # the snapped string: from its post over the nut, then a big curl springing off the neck
    sx = cx
    d.line([(cx - 80, 390), (sx, nut), (sx, nut + 60)], fill=PINK, width=18, joint="curve")
    curl = [(sx, nut + 60), (sx + 60, nut + 110)]
    ccx, ccy = sx + 200, nut + 150
    for i in range(0, 141):
        t = i / 140
        ang = math.radians(200 - 640 * t)
        r = 130 * (1 - 0.7 * t)
        curl.append((ccx + r * math.cos(ang), ccy + r * math.sin(ang)))
    stroke(d, curl, PINK, 32, ow=14)

    img = img.rotate(-24, resample=Image.BICUBIC, center=(C, C))
    return compose(img)


def badge_basement():
    """2. Lucky's Basement: stairs going down under a bare hanging bulb."""
    img, d = layer()
    # staircase descending to the right
    n = 5
    x0, y0 = 270, 480
    step_w, step_h = 86, 60
    pts = [(x0, y0)]
    x, y = x0, y0
    for i in range(n):
        x += step_w
        pts.append((x, y))
        y += step_h
        pts.append((x, y))
    pts += [(x + 60, y), (x + 60, y + 40), (x0, y + 40)]
    poly(d, pts, CYAN)
    # step nosings highlighted white
    x, y = x0, y0
    for i in range(n):
        d.line([(x + 6, y + 10), (x + step_w - 6, y + 10)], fill=WHITE, width=16)
        x += step_w
        y += step_h
    # handrail
    stroke(d, [(x0 + 10, y0 - 120), (x0 + 10 + n * step_w, y0 - 120 + n * step_h)], WHITE, 18, ow=10)
    for i in range(0, n + 1, 2):
        px = x0 + 10 + i * step_w
        py_top = y0 - 120 + i * step_h
        stroke(d, [(px, py_top), (px, y0 + i * step_h)], WHITE, 14, ow=8)

    # hanging bulb (top left, above the stairs)
    bx, by = 370, 345  # centre of the glass globe
    stroke(d, [(bx, 170), (bx, by - 110)], WHITE, 14, ow=10)
    for a in (-10, 30, 70, 110, 150, 190):  # short light dashes below and to the sides
        ax, ay = math.cos(math.radians(a)), math.sin(math.radians(a))
        stroke(d, [(bx + ax * 100, by + ay * 100), (bx + ax * 140, by + ay * 140)], AMBER, 18, ow=8)
    neck = [(bx - 36, by - 95), (bx + 36, by - 95), (bx + 50, by - 30), (bx - 50, by - 30)]
    poly(d, neck, AMBER, w=14)
    circle(d, bx, by, 72, AMBER, w=14)
    d.polygon([(bx - 36 + 7, by - 88), (bx + 36 - 7, by - 88), (bx + 46, by - 30), (bx - 46, by - 30)], fill=AMBER)
    rect(d, bx - 40, by - 140, bx + 40, by - 92, PALE, w=12, radius=8)
    d.line([(bx - 30, by - 116), (bx + 30, by - 116)], fill=CYAN_DK, width=8)
    circle(d, bx - 26, by - 18, 18, WHITE, w=0)
    return compose(img)


def badge_tavern():
    """3. Third Rail Tavern: a beer mug with a lightning bolt."""
    img, d = layer()
    x0, x1, y0, y1 = 290, 640, 330, 800
    # handle
    d.rounded_rectangle([x1 - 60, 420, x1 + 150, 700], radius=90, fill=INK)
    d.rounded_rectangle([x1 - 60 + OW, 420 + OW, x1 + 150 - OW, 700 - OW], radius=76, fill=CYAN)
    d.rounded_rectangle([x1 - 10, 490, x1 + 80, 630], radius=40, fill=INK)
    d.rounded_rectangle([x1 - 10 + OW, 490 + OW, x1 + 80 - OW, 630 - OW], radius=26, fill=(0, 0, 0, 0))
    # body
    rect(d, x0, y0, x1, y1, CYAN, radius=40)
    for gx in (x0 + 80, x1 - 80):  # glass ribs
        d.line([(gx, y0 + 90), (gx, y1 - 50)], fill=CYAN_LT, width=18)
    # foam
    d.rectangle([x0 - 10, 330, x1 + 10, 400], fill=INK)
    for (fx, fy, fr) in [(x0 + 10, 360, 55), (x0 + 100, 310, 75), (x0 + 210, 300, 70), (x1 - 70, 300, 70), (x1 - 10, 360, 50)]:
        circle(d, fx, fy, fr + OW, INK, w=0)
    d.rectangle([x0 + 6, 340, x1 - 6, 395], fill=WHITE)
    for (fx, fy, fr) in [(x0 + 10, 360, 55), (x0 + 100, 310, 75), (x0 + 210, 300, 70), (x1 - 70, 300, 70), (x1 - 10, 360, 50)]:
        circle(d, fx, fy, fr, WHITE, w=0)
    # drip
    d.rounded_rectangle([x0 + 150 - OW, 360, x0 + 190 + OW, 470 + OW], radius=30, fill=INK)
    d.rounded_rectangle([x0 + 150, 360, x0 + 190, 470], radius=20, fill=WHITE)
    # bolt
    bolt = [(500, 430), (395, 610), (468, 610), (420, 760), (575, 560), (495, 560), (555, 430)]
    poly(d, bolt, AMBER)
    return compose(img)


def badge_kettle():
    """4. The Copper Kettle: copper kettle with steam."""
    img, d = layer()
    cx, cy = 455, 640
    # handle arc over the top
    hp = arc_pts(cx, 490, 175, 170, 180, 360)
    stroke(d, hp, CYAN, 36, ow=16)
    # spout
    spout = [(cx + 140, 640), (cx + 230, 520), (cx + 300, 470), (cx + 318, 495), (cx + 268, 560), (cx + 190, 720)]
    poly(d, spout, ORANGE)
    # body
    d.ellipse([cx - 250, cy - 200, cx + 250, cy + 190], fill=INK)
    d.ellipse([cx - 250 + OW, cy - 200 + OW, cx + 250 - OW, cy + 190 - OW], fill=ORANGE)
    # shading crescent
    d.chord([cx - 234, cy - 184, cx + 234, cy + 174], 20, 160, fill=ORANGE_DK)
    d.ellipse([cx - 234, cy - 210, cx + 234, cy + 140], fill=None)
    # highlight
    d.ellipse([cx - 175, cy - 130, cx - 95, cy - 20], fill=ORANGE_LT)
    # base
    rect(d, cx - 200, cy + 150, cx + 200, cy + 200, CYAN, radius=20)
    # lid + knob
    d.chord([cx - 140, cy - 260, cx + 140, cy - 120], 180, 360, fill=INK)
    d.chord([cx - 140 + OW, cy - 260 + OW, cx + 140 - OW, cy - 120 - OW + 20], 180, 360, fill=CYAN)
    circle(d, cx, cy - 215, 34, WHITE, w=14)
    # steam from the spout
    for i, (ox, oy) in enumerate([(0, 0), (70, 30)]):
        sx, sy = cx + 290 + ox, 450 + oy
        pts = []
        for k in range(60):
            t = k / 59
            pts.append((sx + 24 * math.sin(t * 2.6 * math.pi) - 10 * t, sy - 180 * t))
        stroke(d, pts, WHITE, 26, ow=12)
    return compose(img)


def badge_lighthouse():
    """5. Harborlight Club: lighthouse with beams over waves."""
    img, d = layer()
    cx = 512
    ly = 300  # lamp centre
    # beams
    for side in (-1, 1):
        beam = [(cx, ly - 10), (cx + side * 300, ly - 105), (cx + side * 300, ly + 65), (cx, ly + 10)]
        d.polygon(beam, fill=AMBER + (255,))
    # waves behind (bottom)
    # tower
    tower = [(cx - 70, 400), (cx + 70, 400), (cx + 120, 760), (cx - 120, 760)]
    poly(d, tower, WHITE)
    # stripes
    for (ya, yb) in ((480, 545), (620, 690)):
        def xat(y, s):
            return cx + s * (70 + (y - 400) * 50 / 360)
        stripe = [(xat(ya, -1) + 8, ya), (xat(ya, 1) - 8, ya), (xat(yb, 1) - 8, yb), (xat(yb, -1) + 8, yb)]
        d.polygon(stripe, fill=CYAN)
    d.line(tower + [tower[0]], fill=INK, width=OW, joint="curve")
    # door
    rect(d, cx - 30, 680, cx + 30, 760, CYAN_DK, w=12, radius=20)
    # gallery deck
    rect(d, cx - 105, 380, cx + 105, 420, CYAN)
    # lamp room
    rect(d, cx - 62, 250, cx + 62, 385, AMBER)
    for gx in (cx - 20, cx + 20):
        d.line([(gx, 258), (gx, 378)], fill=INK, width=12)
    # roof
    poly(d, [(cx - 90, 255), (cx, 165), (cx + 90, 255)], CYAN)
    circle(d, cx, 160, 18, WHITE, w=12)
    # waves
    for i, y in enumerate((770, 840)):
        pts = wave(175 + i * 40, 850 - i * 40, y, 22, 150)
        stroke(d, pts, CYAN if i == 0 else CYAN_LT, 34, ow=14)
    return compose(img)


def badge_theater():
    """6. Majestic Theater: stage with drawn curtains under a bulb-lit marquee arch."""
    img, d = layer()
    x0, x1, y0, y1 = 220, 804, 300, 780
    # frame
    d.rounded_rectangle([x0 - OW, y0 - OW, x1 + OW, y1 + OW], radius=40, fill=INK)
    d.rounded_rectangle([x0, y0, x1, y1], radius=30, fill=CYAN)
    # stage opening
    ix0, ix1, iy0, iy1 = x0 + 50, x1 - 50, y0 + 100, y1 - 90
    rect(d, ix0, iy0, ix1, iy1, (10, 34, 62), w=OW)
    # spotlight cone + star on the stage
    d.polygon([(512 - 40, iy0 + 8), (512 + 40, iy0 + 8), (512 + 140, iy1 - 8), (512 - 140, iy1 - 8)], fill=(40, 80, 120))
    d.ellipse([512 - 140, iy1 - 50, 512 + 140, iy1 + 10], fill=(70, 120, 160))
    # curtains (swagged to the sides)
    for side in (-1, 1):
        ox = ix0 if side < 0 else ix1
        cur = [(ox, iy0), (ox + (512 - ox) * 0.95, iy0), (ox + (512 - ox) * 0.55, iy0 + 170),
               (ox + (512 - ox) * 0.32, iy0 + 230), (ox + (512 - ox) * 0.42, iy1), (ox, iy1)]
        poly(d, cur, PINK)
        # folds
        for f in (0.12, 0.24):
            fx = ox + (512 - ox) * f
            d.line([(fx, iy0 + 20), (fx + (512 - ox) * 0.05, iy0 + 200), (fx, iy1 - 10)], fill=PINK_DK, width=14, joint="curve")
        # tie-back
        tb = (ox + (512 - ox) * 0.34, iy0 + 215)
        circle(d, tb[0], tb[1], 20, AMBER, w=10)
    # valance (scalloped)
    rect(d, ix0, iy0, ix1, iy0 + 40, PINK)
    for k in range(6):
        sx = ix0 + (ix1 - ix0) * (k + 0.5) / 6
        d.chord([sx - 42, iy0 + 5, sx + 42, iy0 + 75], 0, 180, fill=INK)
        d.chord([sx - 42 + 10, iy0 + 5, sx + 42 - 10, iy0 + 65], 0, 180, fill=PINK)
    d.rectangle([ix0 + 8, iy0 + 8, ix1 - 8, iy0 + 38], fill=PINK)
    # stage floor
    rect(d, x0 + 10, y1 - 90, x1 - 10, y1 - 30, WHITE, w=OW)
    # marquee bulbs along the top band
    n = 9
    for k in range(n):
        bx = x0 + 45 + (x1 - x0 - 90) * k / (n - 1)
        circle(d, bx, y0 + 50, 22, WHITE, w=10)
    # bulbs down the sides
    for k in range(4):
        by = iy0 + 40 + (iy1 - iy0 - 80) * k / 3
        circle(d, x0 + 25, by, 15, WHITE, w=8)
        circle(d, x1 - 25, by, 15, WHITE, w=8)
    return compose(img)


def badge_pavilion():
    """7. Riverfront Pavilion: a tent-roof pavilion over a stage by the river."""
    img, d = layer()
    cx = 512
    roof_y = 480
    peak = (cx, 250)
    l, r = 220, 804
    # poles
    for px in (l + 50, r - 50):
        rect(d, px - 16, roof_y, px + 16, 680, WHITE, w=12)
    # stage
    rect(d, l + 10, 650, r - 10, 720, CYAN, radius=10)
    # light on stage
    d.polygon([(cx - 40, roof_y + 40), (cx + 40, roof_y + 40), (cx + 150, 650), (cx - 150, 650)], fill=(40, 90, 130, 255))
    # roof (stripes)
    n = 6
    pts_edge = [(l + (r - l) * i / n, roof_y) for i in range(n + 1)]
    for i in range(n):
        a, b = pts_edge[i], pts_edge[i + 1]
        col = WHITE if i % 2 == 0 else CYAN
        d.polygon([peak, a, b], fill=col)
    d.line([peak, (l, roof_y), (r, roof_y), peak], fill=INK, width=OW, joint="curve")
    for p in (peak, (l, roof_y), (r, roof_y)):
        dot(d, p, OW / 2, INK)
    # scalloped valance
    for i in range(n):
        a, b = pts_edge[i], pts_edge[i + 1]
        col = WHITE if i % 2 == 0 else CYAN
        d.chord([a[0] - OW / 2, roof_y - 50 - OW / 2, b[0] + OW / 2, roof_y + 50 + OW / 2], 0, 180, fill=INK)
        d.chord([a[0] + 4, roof_y - 50 + 4, b[0] - 4, roof_y + 50 - 4], 0, 180, fill=col)
    # flag
    stroke(d, [peak, (cx, 165)], WHITE, 14, ow=10)
    poly(d, [(cx + 4, 165), (cx + 110, 197), (cx + 4, 230)], AMBER, w=12)
    # river
    for i, y in enumerate((775, 845)):
        stroke(d, wave(200 + i * 50, 824 - i * 50, y, 20, 130), CYAN if i == 0 else CYAN_LT, 32, ow=14)
    return compose(img)


def badge_summit():
    """8. Summit Arena: mountain peak with crossing spotlight beams."""
    img, d = layer()
    # beams from below crossing over the peak
    beams, bd = layer()
    for (bx, tx) in ((410, 750), (614, 274)):
        bd.polygon([(bx - 16, 720), (bx + 16, 720), (tx + 70, 170), (tx - 70, 170)], fill=AMBER + (210,))
        bd.polygon([(bx - 6, 720), (bx + 6, 720), (tx + 26, 170), (tx - 26, 170)], fill=(255, 226, 140, 255))
    # fade the beams out towards the top
    fade = Image.linear_gradient("L").resize((S, S))  # 0 at the top, 255 at the bottom
    fade = fade.point(lambda v: max(0, min(255, int((v - 30) * 255 / 70))))
    beams.putalpha(ImageChops.multiply(beams.getchannel("A"), fade))
    # mountains
    back = [(180, 720), (330, 470), (420, 560), (512, 300), (640, 500), (700, 440), (850, 720)]
    poly(d, back, CYAN)
    # snow cap
    snow = [(512, 300), (588, 418), (552, 400), (520, 440), (488, 400), (448, 420)]
    poly(d, snow, WHITE, w=12)
    small = [(330, 470), (372, 538), (345, 525), (318, 545), (296, 528)]
    poly(d, small, WHITE, w=10)
    # shadow face
    d.polygon([(512, 300), (588, 418), (640, 500), (700, 440), (850, 720 - OW / 2), (600, 720 - OW / 2), (540, 470)],
              fill=CYAN_DK)
    poly(d, snow, WHITE, w=12)
    d.line(back, fill=INK, width=OW, joint="curve")
    # arena dome at the foot
    dx, dy, drx, dry = 512, 735, 170, 125
    d.pieslice([dx - drx - OW, dy - dry - OW, dx + drx + OW, dy + dry + OW], 180, 360, fill=INK)
    d.pieslice([dx - drx, dy - dry, dx + drx, dy + dry], 180, 360, fill=WHITE)
    for mrx in (60, 125):  # meridians
        d.arc([dx - mrx, dy - dry, dx + mrx, dy + dry], 180, 360, fill=CYAN_DK, width=12)
    d.line([(dx, dy - dry), (dx, dy)], fill=CYAN_DK, width=12)
    d.arc([dx - drx, dy - dry * 0.55, dx + drx, dy + dry * 0.55], 180, 360, fill=CYAN_DK, width=12)
    rect(d, 300, dy - 10, 724, dy + 40, CYAN, radius=12)
    return compose(img, light=beams)


def badge_stadium():
    """9. Pacific Coast Stadium: stadium bowl with floodlights and a sunset over the sea."""
    img, d = layer()
    cx = 512
    # sun setting into the sea behind the stadium
    hz = 560
    d.pieslice([cx - 175, hz - 175, cx + 175, hz + 175], 180, 360, fill=INK)
    d.pieslice([cx - 175 + OW, hz - 175 + OW, cx + 175 - OW, hz + 175 - OW], 180, 360, fill=ORANGE)
    d.pieslice([cx - 105, hz - 105, cx + 105, hz + 105], 180, 360, fill=AMBER)
    stroke(d, [(cx - 300, hz + 4), (cx + 300, hz + 4)], CYAN_LT, 18, ow=10)
    # floodlight towers
    for side in (-1, 1):
        tx = cx + side * 262
        stroke(d, [(tx, 700), (tx, 360)], WHITE, 22, ow=12)
        head = [(tx - 62, 280), (tx + 62, 280), (tx + 52, 360), (tx - 52, 360)]
        poly(d, head, WHITE, w=14)
        for gx in (-1, 0, 1):
            for gy in (0, 1):
                circle(d, tx + gx * 32, 302 + gy * 36, 12, AMBER, w=0)
        for a in (-140, -90, -40):
            ax, ay = math.cos(math.radians(a)), math.sin(math.radians(a))
            stroke(d, [(tx + ax * 92, 318 + ay * 82), (tx + ax * 130, 318 + ay * 118)], AMBER, 16, ow=8)
    # bowl: an elliptical top (stands around the pitch) over a curved outer wall
    rx, top_cy, ry, wall = 300, 640, 72, 130
    wall_pts = [(cx - rx, top_cy)] + arc_pts(cx, top_cy + wall, rx * 0.9, ry, 180, 0, 60) + [(cx + rx, top_cy)]
    poly(d, wall_pts, CYAN)
    # arches on the wall
    for k in range(-3, 4):
        ax = cx + k * 74
        ay = top_cy + wall + ry * math.sqrt(max(0.0, 1 - (k * 74 / (rx * 0.9)) ** 2)) - 40
        d.rounded_rectangle([ax - 20, ay - 70, ax + 20, ay - 5], radius=20, fill=CYAN_DK)
    # top rim, stands and pitch
    d.ellipse([cx - rx - OW / 2, top_cy - ry - OW / 2, cx + rx + OW / 2, top_cy + ry + OW / 2], fill=INK)
    d.ellipse([cx - rx + OW / 2, top_cy - ry + OW / 2, cx + rx - OW / 2, top_cy + ry - OW / 2], fill=WHITE)
    d.ellipse([cx - rx + 34, top_cy - ry + 20, cx + rx - 34, top_cy + ry - 14], fill=CYAN)
    d.ellipse([cx - rx + 100, top_cy - ry + 40, cx + rx - 100, top_cy + ry - 30], fill=CYAN_DK)
    d.line([(cx, top_cy - ry + 46), (cx, top_cy + ry - 36)], fill=WHITE, width=8)
    return compose(img)


BADGES = [
    badge_broken_string,
    badge_basement,
    badge_tavern,
    badge_kettle,
    badge_lighthouse,
    badge_theater,
    badge_pavilion,
    badge_summit,
    badge_stadium,
]


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    out_dir = sys.argv[1] if len(sys.argv) > 1 else os.path.join(here, "Rock Band 1")
    os.makedirs(out_dir, exist_ok=True)
    for i, fn in enumerate(BADGES, start=1):
        path = os.path.join(out_dir, f"{i}.png")
        fn().save(path)
        print("wrote", path)


if __name__ == "__main__":
    main()
