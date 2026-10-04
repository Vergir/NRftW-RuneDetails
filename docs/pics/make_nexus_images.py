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

# The whole tooltip panel, and the lines the mod adds, of the Air Dodge tooltip in the three modes.
PANEL = {"01_inv_off": (875, 298, 1555, 785), "02_inv_brief": (875, 298, 1555, 824),
         "03_inv_detailed": (875, 298, 1555, 934)}
ADDED = {"02_inv_brief": (902, 661, 1205, 692), "03_inv_detailed": (902, 552, 1500, 800)}

# Gallery shots: a 16:9 window of the frame, the added lines boxed, the caption and the free corner it goes in.
GALLERY = [
    ("2_inventory.jpg", "03_inv_detailed", (140, 170, 1740, 1070), [ADDED["03_inv_detailed"]], "Item tooltip, Detailed", "rb"),
    ("3_runes_menu.jpg", "04_runes_menu", (60, 240, 1800, 1219), [(1000, 796, 1690, 1078)], "Runes menu", "rt"),
    ("4_vendor.jpg", "05_vendor", (100, 130, 1980, 1188), [(1278, 528, 1904, 735)], "Vendor", "rt"),
    ("5_utility.jpg", "06_utility", (140, 170, 1740, 1070), [(900, 593, 1552, 760)], "Utility rune", "rb"),
    ("6_settings.jpg", "07_settings", (130, 206, 2534, 1559), [(196, 1132, 1432, 1462)], "Options > Gameplay", "rb"),
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


def brighten(im, k=1.1):
    """The style guide's +10% levels on crops, so the grey notes read at column width."""
    return ImageEnhance.Brightness(im).enhance(k)


def box(img, b, scale=1.0, dx=0, dy=0, pad=10, ref=None):
    """The highlight: a rounded gold outline, 5 px and radius 14 at a 1080 px tall image (ref = that image's height)."""
    k = (ref or img.height) / 1080
    l, t, r, bt = b
    ImageDraw.Draw(img).rounded_rectangle((round(l * scale) + dx - pad, round(t * scale) + dy - pad,
                                           round(r * scale) + dx + pad, round(bt * scale) + dy + pad),
                                          round(14 * k), outline=GOLD, width=max(3, round(5 * k)))


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
    lab_h = round(size * 1.6)
    img = Image.new("RGBA", (width, lab_h + max(c.height for c in crops)), (0, 0, 0, 0))
    x = 0
    for n, lbl, c in zip(names, labels, crops):
        img.paste(c, (x, lab_h))
        if n in ADDED:
            box(img, (ADDED[n][0] - PANEL[n][0], ADDED[n][1] - PANEL[n][1], ADDED[n][2] - PANEL[n][0],
                      ADDED[n][3] - PANEL[n][1]), scale, x, lab_h, pad=8, ref=H)
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


def inline_image():
    """The one image inside the description: Brief and Detailed, large enough to read at column width."""
    group, _ = panels(["02_inv_brief", "03_inv_detailed"], ["Brief", "Detailed"], 1400, 40, 56)
    m = 30
    img = Image.new("RGBA", (group.width + 2 * m, group.height + 2 * m), (34, 32, 30, 255))
    img.alpha_composite(group, (m, m))
    save(img, "inline.jpg")


def gallery():
    for out, name, win, boxes, cap, corner in GALLERY:
        img = brighten(frame(name).crop(win)).convert("RGBA")
        for b in boxes:
            box(img, (b[0] - win[0], b[1] - win[1], b[2] - win[0], b[3] - win[1]))
        m = img.height // 40 + 12
        x = img.width - m if corner[0] == "r" else m
        y = img.height - m if corner[1] == "b" else m
        label(img, cap, (x, y), max(56, img.height // 16), anchor=corner[0] + ("d" if corner[1] == "b" else "a"))
        save(img, out)


def thumbs(img):
    """The listing-tile test (style guide section 7): 300 and 170 px renders next to the outputs."""
    for w, h in ((300, 169), (170, 96)):
        img.convert("RGB").resize((w, h), Image.LANCZOS).save(os.path.join(OUT, f"_tile_{w}.png"))
    g = np.asarray(img.convert("L"), dtype=np.float32) / 255
    print(f"tile brightness {g.mean():.3f} (reference tiles 0.24), contrast {g.std():.3f}")


if __name__ == "__main__":
    tile = main_image()
    inline_image()
    gallery()
    if "--thumbs" in sys.argv:
        thumbs(tile)
