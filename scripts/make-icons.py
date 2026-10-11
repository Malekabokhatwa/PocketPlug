#!/usr/bin/env python3
"""Builds PocketPlug's PNG assets from Lucide SVGs (ISC). Needs rsvg-convert and Pillow.

App icons are full squares (the phone masks them to rounded corners): a vertical gradient with a white glyph.
Also draws the compass ring and circle mask used for customer photos.
"""
import subprocess, pathlib, io
from PIL import Image, ImageDraw

ROOT = pathlib.Path(__file__).resolve().parent.parent
SVG = ROOT / "assets/icons/svg"
OUT = ROOT / "assets/icons"
SIZE = 256

APPS = {
    # name: (lucide icon, top color, bottom color)
    "app_settings": ("settings", (98, 104, 118), (52, 56, 66)),
    "app_bank": ("landmark", (64, 196, 120), (24, 122, 70)),
    "app_payroll": ("wallet", (245, 166, 35), (196, 102, 20)),
    "contact_aab": ("chart-column", (72, 140, 240), (36, 80, 170)),
}
GLYPHS = {  # small white glyphs used in notifications
    "glyph_bell": "bell",
}

def glyph(name, px, color="#ffffff"):
    svg = (SVG / f"{name}.svg").read_text().replace("currentColor", color)
    png = subprocess.run(["rsvg-convert", "-w", str(px), "-h", str(px)], input=svg.encode(), capture_output=True, check=True).stdout
    return Image.open(io.BytesIO(png)).convert("RGBA")

def gradient(top, bottom):
    img = Image.new("RGBA", (SIZE, SIZE))
    d = ImageDraw.Draw(img)
    for y in range(SIZE):
        t = y / (SIZE - 1)
        d.line([(0, y), (SIZE, y)], fill=tuple(int(a + (b - a) * t) for a, b in zip(top, bottom)) + (255,))
    return img

for out, (icon, top, bottom) in APPS.items():
    img = gradient(top, bottom)
    g = glyph(icon, 150)
    img.alpha_composite(g, ((SIZE - 150) // 2, (SIZE - 150) // 2))
    img.save(OUT / f"{out}.png")

for out, icon in GLYPHS.items():
    glyph(icon, 128).save(OUT / f"{out}.png")

# Compass: green ring with a transparent middle, and a white disc used as a mask for the photo.
S = 4 * SIZE  # supersample for smooth edges
ring = Image.new("RGBA", (S, S), (0, 0, 0, 0))
ImageDraw.Draw(ring).ellipse([0, 0, S - 1, S - 1], fill=(70, 210, 110, 255))
ImageDraw.Draw(ring).ellipse([S * 0.1, S * 0.1, S * 0.9 - 1, S * 0.9 - 1], fill=(0, 0, 0, 0))
ring.resize((SIZE, SIZE), Image.LANCZOS).save(OUT / "ring.png")

disc = Image.new("RGBA", (S, S), (0, 0, 0, 0))
ImageDraw.Draw(disc).ellipse([0, 0, S - 1, S - 1], fill=(255, 255, 255, 255))
disc.resize((SIZE, SIZE), Image.LANCZOS).save(OUT / "disc.png")
print("icons:", sorted(p.name for p in OUT.glob("*.png")))

# ---- Backroom (the PocketPlug mod menu) ----
# Glyphs: white Lucide icons at 2x their usual 24 px display size.
BACKROOM_GLYPHS = [
    "house", "package", "banknote", "user", "sun", "map-pin", "users", "building-2", "trophy", "settings-2", "plug",
    "search", "chevron-right", "chevron-left", "chevron-up", "chevron-down", "star", "check", "x", "plus", "minus", "car", "clock", "cloud-rain",
    "shield", "heart", "zap", "terminal", "trash-2", "save", "eye", "laptop", "history", "landmark", "wallet",
    "sparkles", "gauge", "footprints", "moon", "flag", "lock", "skull", "refresh-cw",
]
for name in BACKROOM_GLYPHS:
    glyph(name, 48).save(OUT / f"br_{name}.png")

def rounded(size, radius, fill=(255, 255, 255, 255)):
    big = Image.new("RGBA", (size * 4, size * 4), (0, 0, 0, 0))
    ImageDraw.Draw(big).rounded_rectangle([0, 0, size * 4 - 1, size * 4 - 1], radius=radius * 4, fill=fill)
    return big.resize((size, size), Image.LANCZOS)

# One rounded square with a 32 px corner; the UI picks any radius with Image.pixelsPerUnitMultiplier = 32 / radius.
rounded(128, 32).save(OUT / "br_round.png")

# Soft drop shadow: a rounded square blurred into a 48 px falloff (9-slice border 64).
from PIL import ImageFilter
shadow = Image.new("RGBA", (192, 192), (0, 0, 0, 0))
ImageDraw.Draw(shadow).rounded_rectangle([48, 48, 143, 143], radius=24, fill=(0, 0, 0, 255))
shadow.filter(ImageFilter.GaussianBlur(18)).save(OUT / "br_shadow.png")

# Hold-to-open ring (drawn as a radial fill) and its faint track.
for out, alpha in (("br_ring", 255), ("br_ring_track", 70)):
    big = Image.new("RGBA", (512, 512), (0, 0, 0, 0))
    d = ImageDraw.Draw(big)
    d.ellipse([8, 8, 503, 503], fill=(255, 255, 255, alpha))
    d.ellipse([60, 60, 451, 451], fill=(0, 0, 0, 0))
    big.resize((128, 128), Image.LANCZOS).save(OUT / f"{out}.png")

# Selection frame for grid tiles: a 2 px white outline with 10 px corners (9-slice border 16, used at 1:1).
big = Image.new("RGBA", (256, 256), (0, 0, 0, 0))
d = ImageDraw.Draw(big)
d.rounded_rectangle([0, 0, 255, 255], radius=40, fill=(255, 255, 255, 255))
d.rounded_rectangle([8, 8, 247, 247], radius=32, fill=(0, 0, 0, 0))
big.resize((64, 64), Image.LANCZOS).save(OUT / "br_frame.png")

# Backroom laptop lock screen: graphite with a soft vignette and the PocketPlug plug glyph in a thin ring.
W, H = 512, 320
screen = Image.new("RGBA", (W, H), (15, 16, 18, 255))
vig = Image.new("L", (W, H), 0)
ImageDraw.Draw(vig).ellipse([-W * 0.2, -H * 0.4, W * 1.2, H * 1.4], fill=60)
vig = vig.filter(ImageFilter.GaussianBlur(60))
screen = Image.composite(Image.new("RGBA", (W, H), (40, 43, 48, 255)), screen, vig)
ring = Image.new("RGBA", (440, 440), (0, 0, 0, 0))
ImageDraw.Draw(ring).ellipse([0, 0, 439, 439], outline=(242, 243, 245, 255), width=14)
ring = ring.resize((110, 110), Image.LANCZOS)
screen.alpha_composite(ring, ((W - 110) // 2, (H - 110) // 2))
plug = glyph("plug", 56, "#F2F3F5")
screen.alpha_composite(plug, ((W - 56) // 2, (H - 56) // 2))
screen.save(OUT / "br_lockscreen.png")
