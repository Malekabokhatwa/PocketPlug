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
