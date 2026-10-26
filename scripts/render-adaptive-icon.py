#!/usr/bin/env python3
"""Build Android adaptive-icon layers from the transparent gpsTrainer artwork.

The foreground mark stays inside the center 66dp of the 108dp canvas (the
adaptive-icon safe zone). The background is a separate flat color. A white
monochrome layer uses the same alpha for themed icons. Legacy mipmaps and the
Play 512 icon are the same composite, and circle/squircle previews are written
beside the source art.
"""

from __future__ import annotations

import math
from pathlib import Path

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "src/GpsTrainer.App/Resources/AppIcon/appicon.png"
ANDROID = ROOT / "src/GpsTrainer.App/Platforms/Android/Resources"
ICON_DIR = ROOT / "src/GpsTrainer.App/Resources/AppIcon"
BACKGROUND = (244, 248, 251, 255)  # #F4F8FB
SAFE_FRACTION = 66 / 108

FOREGROUND_DP = {
    "mdpi": 108,
    "hdpi": 162,
    "xhdpi": 216,
    "xxhdpi": 324,
    "xxxhdpi": 432,
}
LEGACY_DP = {
    "mdpi": 48,
    "hdpi": 72,
    "xhdpi": 96,
    "xxhdpi": 144,
    "xxxhdpi": 192,
}


def opaque_bbox(image: Image.Image, threshold: int = 16) -> tuple[int, int, int, int]:
    alpha = image.getchannel("A")
    mask = alpha.point(lambda value: 255 if value >= threshold else 0)
    box = mask.getbbox()
    if box is None:
        raise SystemExit("The source icon has no opaque pixels.")
    return box


def foreground(source: Image.Image, size: int) -> Image.Image:
    """Scale the mark so every solid pixel sits inside the center 66dp circle."""
    box = opaque_bbox(source)
    mark = source.crop(box)
    pixels = mark.load()
    center_x = (mark.width - 1) / 2
    center_y = (mark.height - 1) / 2
    farthest = 1.0
    for y in range(mark.height):
        for x in range(mark.width):
            if pixels[x, y][3] < 200:
                continue
            farthest = max(farthest, math.hypot(x - center_x, y - center_y))
    # One pixel of inset keeps Lanczos fringes inside the safe circle.
    radius = max(1.0, size * SAFE_FRACTION / 2 - 1.5)
    scale = radius / farthest
    resized = mark.resize(
        (max(1, round(mark.width * scale)), max(1, round(mark.height * scale))),
        Image.Resampling.LANCZOS,
    )
    canvas = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    left = int(round((size - resized.width) / 2))
    top = int(round((size - resized.height) / 2))
    canvas.alpha_composite(resized, (left, top))
    return canvas


def composite(layer: Image.Image) -> Image.Image:
    base = Image.new("RGBA", layer.size, BACKGROUND)
    base.alpha_composite(layer)
    return base


def monochrome(layer: Image.Image) -> Image.Image:
    alpha = layer.getchannel("A")
    white = Image.new("RGBA", layer.size, (255, 255, 255, 0))
    white.putalpha(alpha)
    return white


def circle_mask(size: int) -> Image.Image:
    mask = Image.new("L", (size, size), 0)
    draw = ImageDraw.Draw(mask)
    draw.ellipse((0, 0, size - 1, size - 1), fill=255)
    return mask


def squircle_mask(size: int, power: float = 4.0) -> Image.Image:
    """Superellipse squircle. The radius reaches the canvas edge."""
    mask = Image.new("L", (size, size), 0)
    pixels = mask.load()
    radius = (size - 1) / 2
    for y in range(size):
        for x in range(size):
            nx = (x - radius) / radius
            ny = (y - radius) / radius
            if abs(nx) ** power + abs(ny) ** power <= 1:
                pixels[x, y] = 255
    return mask


def masked_preview(icon: Image.Image, mask: Image.Image, name: str) -> None:
    icon = icon.resize(mask.size, Image.Resampling.LANCZOS)
    shaped = Image.new("RGBA", mask.size, (0, 0, 0, 0))
    shaped.paste(icon, (0, 0))
    shaped.putalpha(mask)
    canvas = Image.new("RGBA", (mask.width + 80, mask.height + 80), (236, 236, 236, 255))
    canvas.alpha_composite(shaped, (40, 40))
    canvas.save(ICON_DIR / name)


def assert_safe(layer: Image.Image) -> None:
    size = layer.width
    radius = size * SAFE_FRACTION / 2
    center = (size - 1) / 2
    pixels = layer.load()
    farthest = 0.0
    for y in range(size):
        for x in range(size):
            if pixels[x, y][3] < 200:
                continue
            distance = math.hypot(x - center, y - center)
            farthest = max(farthest, distance)
    if farthest > radius + 1.0:
        raise SystemExit(f"Foreground spills the safe zone: {farthest:.1f}px > {radius:.1f}px")
    print(f"safe zone ok: farthest solid pixel {farthest:.1f}px, radius {radius:.1f}px")


def write_xml() -> None:
    xml = """<?xml version="1.0" encoding="utf-8"?>
<adaptive-icon xmlns:android="http://schemas.android.com/apk/res/android">
    <background android:drawable="@color/appicon_background" />
    <foreground android:drawable="@mipmap/appicon_foreground" />
    <monochrome android:drawable="@drawable/appicon_monochrome" />
</adaptive-icon>
"""
    folder = ANDROID / "mipmap-anydpi-v26"
    folder.mkdir(parents=True, exist_ok=True)
    (folder / "appicon.xml").write_text(xml)
    (folder / "appicon_round.xml").write_text(xml)


def main() -> None:
    source = Image.open(SOURCE).convert("RGBA")
    master = foreground(source, 1024)
    assert_safe(master)
    play = composite(master.resize((512, 512), Image.Resampling.LANCZOS))
    play.save(ICON_DIR / "icon-512.png")

    for density, size in FOREGROUND_DP.items():
        layer = foreground(source, size)
        folder = ANDROID / f"mipmap-{density}"
        folder.mkdir(parents=True, exist_ok=True)
        layer.save(folder / "appicon_foreground.png")
        mono_dir = ANDROID / f"drawable-{density}"
        mono_dir.mkdir(parents=True, exist_ok=True)
        monochrome(layer).save(mono_dir / "appicon_monochrome.png")

    legacy_master = composite(master)
    for density, size in LEGACY_DP.items():
        folder = ANDROID / f"mipmap-{density}"
        folder.mkdir(parents=True, exist_ok=True)
        icon = legacy_master.resize((size, size), Image.Resampling.LANCZOS)
        icon.save(folder / "appicon.png")
        circled = Image.new("RGBA", icon.size, (0, 0, 0, 0))
        circled.paste(icon, (0, 0))
        circled.putalpha(circle_mask(size))
        circled.save(folder / "appicon_round.png")

    preview = composite(master.resize((512, 512), Image.Resampling.LANCZOS))
    masked_preview(preview, circle_mask(512), "launcher-circle.png")
    masked_preview(preview, squircle_mask(512), "launcher-squircle.png")
    write_xml()
    print("wrote adaptive layers, legacy mipmaps, play icon, and mask previews")


if __name__ == "__main__":
    main()
