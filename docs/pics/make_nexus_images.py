"""Builds the Nexus images in docs/pics/nexus from the raw screenshots in docs/pics (shot list:
nexus-pages/shots/RuneDetails/SHOTLIST.md).

Needs Pillow and numpy (pip install pillow numpy). Usage: python make_nexus_images.py [--thumbs]
Raw screenshots (git-ignored) are 2878 px wide with black bars above and below a 2878x1620 game frame; the frame top
is detected. All boxes below are in frame pixels (raw resolution, bars removed): left, top, right, bottom.
House rules (nexus-pages/house-rules.md): captions and a rounded box are the only highlighting; no dimming, no zoom
insets, no arrows; the main image is 16:9 with the mod name as the biggest text at 1/6 of the height.
"""
import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageEnhance, ImageFilter, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "nexus")
FONT = "C:/Windows/Fonts/segoeuib.ttf"
GOLD = (216, 201, 163)
W, H = 1920, 1080
FW, FH = 2878, 1620

# The whole tooltip panel, and a rough box around the lines the mod adds (fit() tightens it to the text), of the Air
# Dodge tooltip in the three modes.
PANEL = {"01_inv_off": (875, 298, 1555, 785), "02_inv_brief": (875, 298, 1555, 824),
         "03_inv_detailed": (875, 298, 1555, 934)}
ADDED = {"02_inv_brief": (890, 648, 1540, 700), "03_inv_detailed": (890, 545, 1540, 805)}

# Gallery shots: full frames (window None) or a window of the frame; the subject kept bright on a full frame (None = no
# dimming); the boxes around the mod's lines (fit = tighten to the text, only on the dark tooltip panels); the caption,
# placed in frame pixels next to the subject.
GALLERY = [
    ("2_inventory.jpg", "03_inv_detailed", None, PANEL["03_inv_detailed"], [(ADDED["03_inv_detailed"], True)],
     "Inventory", (875, 975), "la"),
    ("3_runes_menu.jpg", "04_runes_menu", None, (790, 570, 1720, 1240), [((1000, 796, 1690, 1078), False)],
     "Runes menu", (1008, 1275), "la"),
    ("4_vendor.jpg", "05_vendor", None, (1253, 269, 1932, 838), [((1270, 520, 1915, 745), True)],
     "Vendor", (1253, 880), "la"),
    ("5_utility.jpg", "06_utility", None, (875, 297, 1555, 891), [((890, 585, 1555, 770), True)],
     "Utility rune", (875, 932), "la"),
    ("6_settings.jpg", "07_settings", (130, 206, 2534, 1559), None, [((196, 1132, 1432, 1462), False)],
     "Options > Gameplay", (2534 - 60, 1559 - 50), "rd"),
]


def font(size):
    return ImageFont.truetype(FONT, size)


_cache = {}


def frame(name):
    """The 2878x1620 game frame of a raw screenshot."""
    if name not in _cache:
        im = Image.open(os.path.join(HERE, name + ".png")).convert("RGB")
        rows = np.asarray(im.convert("L")).astype(float).mean(axis=1)
        top = int(np.where(rows > 4)[0][0])
        _cache[name] = im.crop((0, top, FW, top + FH))
    return _cache[name]


def fit(name, rough):
    """Tighten a rough box to the text inside it (pixels clearly brighter than the dark tooltip panel), so the outline
    sits at the same distance from the text on every side."""
    l, t, r, b = rough
    g = np.asarray(frame(name).crop(rough).convert("L")).astype(float)
    ink = g > max(105, np.median(g) + 55)
    rows, cols = np.where(ink.sum(axis=1) >= 2)[0], np.where(ink.sum(axis=0) >= 2)[0]
    if len(rows) == 0:
        return rough
    return (l + cols[0], t + rows[0], l + cols[-1] + 1, t + rows[-1] + 1)


FOOTER = 68  # the tooltip's "Sell value" / "Buy" footer, frame pixels


def panel(name, approx, r=25):
    """The tooltip panel near a rough box (each edge within r px): left, right and top snap to the strongest straight
    edge; the bottom is the footer's top (a step up in brightness over flat rows) plus the footer's fixed height."""
    a = np.asarray(frame(name).convert("L")).astype(float)
    l, t, rt, b = approx
    gx, gy = np.abs(np.diff(a, axis=1)), np.abs(np.diff(a, axis=0))
    cols = gx[t + 20:b - 20, :].mean(axis=0)
    rows = gy[:, l + 20:rt - 20].mean(axis=1)
    l = l - r + int(np.argmax(cols[l - r:l + r])) + 1
    rt = rt - r + int(np.argmax(cols[rt - r:rt + r])) + 1
    t = t - r + int(np.argmax(rows[t - r:t + r])) + 1
    band = a[:, l + 6:rt - 6]
    mean, std = band.mean(axis=1), band.std(axis=1)
    for y in range(t + 150, min(FH - FOOTER, b + 120)):
        before, after = band[y - 14:y - 3], band[y + 3:y + 18]
        if after.mean() - before.mean() > 8 and std[y - 14:y - 3].max() < 7 and std[y + 3:y + 18].max() < 7:
            return (l, t, rt, y + FOOTER)
    return (l, t, rt, b)


def brighten(im, k=1.1):
    """The style guide's +10% levels on crops, so the grey notes read at column width."""
    return ImageEnhance.Brightness(im).enhance(k)


def box(img, b, scale=1.0, dx=0, dy=0, gap=12, ref=None):
    """The highlight: a rounded gold outline, 5 px wide, radius 14 and 12 px clear of the text at a 1080 px tall image
    (ref = that image's height). PIL draws the outline inside the rectangle, so the rectangle grows by gap + width."""
    k = (ref or img.height) / 1080
    w = max(3, round(5 * k))
    pad = round(gap * k) + w
    l, t, r, bt = b
    ImageDraw.Draw(img).rounded_rectangle((round(l * scale) + dx - pad, round(t * scale) + dy - pad,
                                           round(r * scale) + dx + pad, round(bt * scale) + dy + pad),
                                          round(14 * k), outline=GOLD, width=w)


def boxed_over_text(im, b):
    """box() on a tooltip crop, with the neighbouring lines' text drawn over the outline: the game's lines are too close
    for a centred box to clear the descenders of the line above."""
    out = im.copy().convert("RGBA")
    box(out, b, gap=10, ref=H)
    ink = np.asarray(im.convert("L")) > 110
    l, t, r, bt = b
    ink[t:bt, l:r] = False  # the boxed text itself stays as it is
    out.paste(im.convert("RGBA"), (0, 0), Image.fromarray((ink * 255).astype(np.uint8)).filter(ImageFilter.MaxFilter(3)))
    return out


def spotlight(img, rect, dim=0.6):
    """Full frames: the screen stays visible for context, everything but the subject at 60% brightness."""
    dark = ImageEnhance.Brightness(img).enhance(dim)
    mask = Image.new("L", img.size, 0)
    ImageDraw.Draw(mask).rounded_rectangle(rect, 10, fill=255)
    return Image.composite(img, dark, mask.filter(ImageFilter.GaussianBlur(4)))


def label(img, text, xy, size, anchor="la"):
    """White text on a dark pill."""
    d = ImageDraw.Draw(img, "RGBA")
    f = font(size)
    l, t, r, b = d.textbbox(xy, text, font=f, anchor=anchor)
    p = size // 4
    d.rounded_rectangle((l - p, t - p, r + p, b + p), p, fill=(0, 0, 0, 190))
    d.text(xy, text, font=f, fill="white", anchor=anchor)


def outlined(img, text, xy, size, anchor="mm"):
    """The title: white with a dark outline and a soft shadow, straight on the scene."""
    f = font(size)
    sh = Image.new("RGBA", img.size, (0, 0, 0, 0))
    ImageDraw.Draw(sh).text((xy[0] + 6, xy[1] + 8), text, font=f, fill=(0, 0, 0, 200), anchor=anchor)
    img.alpha_composite(sh.filter(ImageFilter.GaussianBlur(10)))
    ImageDraw.Draw(img).text(xy, text, font=f, fill="white", anchor=anchor, stroke_width=4, stroke_fill=(20, 20, 20))


def shadowed_paste(dst, src, xy):
    x, y = xy
    sh = Image.new("RGBA", dst.size, (0, 0, 0, 0))
    ImageDraw.Draw(sh).rectangle((x + 6, y + 10, x + src.width + 6, y + src.height + 10), fill=(0, 0, 0, 210))
    dst.alpha_composite(sh.filter(ImageFilter.GaussianBlur(14)))
    dst.paste(src, (x, y))


def save(img, name):
    os.makedirs(OUT, exist_ok=True)
    img.convert("RGB").save(os.path.join(OUT, name), quality=92)
    print(f"{name:20} {img.width}x{img.height} {os.path.getsize(os.path.join(OUT, name)) // 1024:5} KB")


def panels(names, labels, width, gap, size):
    """The tooltip panels side by side, top-aligned, each with its label above it; returns (image, scale)."""
    crops = [brighten(frame(n).crop(PANEL[n])) for n in names]
    scale = (width - gap * (len(crops) - 1)) / sum(c.width for c in crops)
    crops = [c.resize((round(c.width * scale), round(c.height * scale)), Image.LANCZOS) for c in crops]
    for i, n in enumerate(names):
        if n in ADDED:
            a = fit(n, ADDED[n])
            a = tuple(round((v - o) * scale) for v, o in zip(a, PANEL[n][:2] * 2))
            crops[i] = boxed_over_text(crops[i], a)
    lab_h = round(size * 1.6)
    img = Image.new("RGBA", (width, lab_h + max(c.height for c in crops)), (0, 0, 0, 0))
    x = 0
    for n, lbl, c in zip(names, labels, crops):
        img.paste(c, (x, lab_h))
        label(img, lbl, (x + size // 4, lab_h - size // 2), size, anchor="lb")
        x += c.width + gap
    return img, scale


def main_image():
    """Main image = listing tile: Vanilla / Brief / Detailed of one tooltip on the scene, the name on top."""
    bg = frame("08_scene").resize((W, H), Image.LANCZOS).filter(ImageFilter.GaussianBlur(4))
    img = ImageEnhance.Brightness(bg).enhance(0.9).convert("RGBA")
    outlined(img, "Rune Details", (W // 2, 132), 250)
    group, _ = panels(["01_inv_off", "02_inv_brief", "03_inv_detailed"], ["Vanilla", "Brief", "Detailed"],
                      W - 2 * 48, 40, 68)
    # The panels have different heights, so shadow each one separately, below the title band.
    y = 272
    shadow = Image.new("RGBA", img.size, (0, 0, 0, 0))
    shadow.paste((0, 0, 0, 200), (48 + 6, y + 10), group.split()[3])
    img.alpha_composite(shadow.filter(ImageFilter.GaussianBlur(14)))
    img.alpha_composite(group, (48, y))
    save(img, "1_main.jpg")
    return img


def gallery():
    for out, name, win, subject, boxes, cap, (cx, cy), anchor in GALLERY:
        full = win is None
        win = win or (0, 0, FW, FH)
        img = brighten(frame(name).crop(win))
        k = W / img.width if full else 1.0  # full frames go to 1920x1080; windows keep their pixels (all wider than 1280)
        if full:
            img = img.resize((W, H), Image.LANCZOS)
        if subject:
            img = spotlight(img, tuple(round((v - o) * k) for v, o in zip(subject, (win[0], win[1], win[0], win[1]))))
        img = img.convert("RGBA")
        for b, tighten in boxes:
            b = fit(name, b) if tighten else b
            box(img, (b[0] - win[0], b[1] - win[1], b[2] - win[0], b[3] - win[1]), k)
        size = max(56, img.height // 16)
        label(img, cap, (round((cx - win[0]) * k) + size // 4, round((cy - win[1]) * k)), size, anchor=anchor)
        save(img, out)


def collage(out, shots, columns, caption):
    """Many tooltips as they look in game: each panel at its own pixels, packed into the shortest column, on the
    blurred scene; one caption. shots = [(file, rough panel box)]."""
    crops = [brighten(frame(n).crop(panel(n, approx))) for n, approx in shots]
    gap, m, cap = 36, 48, 96
    cw = max(c.width for c in crops)
    heights = [0] * columns
    places = []
    for c in crops:
        i = heights.index(min(heights))
        places.append((m + i * (cw + gap), m + cap + heights[i]))
        heights[i] += c.height + gap
    size = (m * 2 + columns * cw + (columns - 1) * gap, m * 2 + cap + max(heights) - gap)
    bg = frame("08_scene").resize(size, Image.LANCZOS).filter(ImageFilter.GaussianBlur(6))
    img = ImageEnhance.Brightness(bg).enhance(0.75).convert("RGBA")
    for c, xy in zip(crops, places):
        shadowed_paste(img, c.convert("RGBA"), xy)
    label(img, caption, (m + 16, m + cap // 2 - 6), 72, anchor="lm")
    save(img, out)


def thumbs(img):
    """The listing-tile test (style guide section 7): 300 and 170 px renders next to the outputs."""
    for w, h in ((300, 169), (170, 96)):
        img.convert("RGB").resize((w, h), Image.LANCZOS).save(os.path.join(OUT, f"_tile_{w}.png"))
    g = np.asarray(img.convert("L"), dtype=np.float32) / 255
    print(f"tile brightness {g.mean():.3f} (reference tiles 0.24), contrast {g.std():.3f}")


if __name__ == "__main__":
    tile = main_image()
    gallery()
    if "--thumbs" in sys.argv:
        thumbs(tile)
