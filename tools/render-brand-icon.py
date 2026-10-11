#!/usr/bin/env python3
"""Rasterize the original Lockvyn vector geometry for Windows and Chromium."""
from pathlib import Path
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
SCALE = 2048 / 108
canvas = Image.new("RGBA", (2048, 2048))
draw = ImageDraw.Draw(canvas)

def point(p):
    return tuple(round(value * SCALE) for value in p)

def curve(start, first, second, end):
    return [tuple((1-t)**3*start[k]+3*(1-t)**2*t*first[k]+3*(1-t)*t*t*second[k]+t**3*end[k]
                  for k in range(2)) for t in [i/128 for i in range(1,129)]]

purple = "#7762DF"
draw.rounded_rectangle((0, 0, 2047, 2047), radius=round(25*SCALE), fill=purple)
shield = [(54,17),(82,29),(82,51)]
shield += curve((82,51),(82,69),(71,83),(54,92))
shield += curve((54,92),(37,83),(26,69),(26,51))
shield += [(26,29)]
draw.polygon([point(p) for p in shield], fill="white")
draw.ellipse((*point((44,35)),*point((64,55))), fill=purple)
draw.polygon([point(p) for p in [(50,51),(58,51),(62,70),(46,70)]], fill=purple)
icon = canvas.resize((512,512), Image.Resampling.LANCZOS)
assets = ROOT / "src/BoshaVault.Windows/Assets"
icon.save(assets / "lockvyn-mark.png")
icon.save(assets / "lockvyn.ico", format="ICO", sizes=[(n,n) for n in [16,24,32,48,64,128,256]])
extension = ROOT / "browser-extension/icons"
extension.mkdir(exist_ok=True)
for size in [16,32,48,128]:
    icon.resize((size,size), Image.Resampling.LANCZOS).save(extension / f"lockvyn-{size}.png")
print("Lockvyn shield/keyhole icon rendered at all required sizes.")
