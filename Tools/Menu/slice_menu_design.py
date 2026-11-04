#!/usr/bin/env python3
"""Cuts the main menu design into transparent UI sprites: python3 Tools/Menu/slice_menu_design.py"""
from pathlib import Path
import cv2, numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "Assets/Resources/UI"
SCALE = 2     # the design is 852 px wide, the phone 1170
src = np.asarray(Image.open(Path(__file__).with_name("menu-design.webp")).convert("RGB")).astype(np.uint8)

def rim_mask(rgb):
    """Everything inside the closed gold rim: flood the outside, stopping at gold."""
    r, g, b = [rgb[..., i].astype(int) for i in range(3)]
    gold = (r > 135) & (g > 85) & (b < 135) & (r > b + 40)
    kernel = np.ones((3, 3), np.uint8)
    closed = cv2.dilate(gold.astype(np.uint8), kernel)      # a thin rim can have gaps the flood would leak through
    pad = cv2.copyMakeBorder(closed, 1, 1, 1, 1, cv2.BORDER_CONSTANT, value=0)
    flood = np.zeros((pad.shape[0] + 2, pad.shape[1] + 2), np.uint8)
    cv2.floodFill(pad, flood, (0, 0), 2)
    return cv2.erode((pad[1:-1, 1:-1] != 2).astype(np.uint8), kernel).astype(np.float32)

def stadium(h, w, inset=2.0):
    """A capsule filling the crop, for rims too thin to find by colour."""
    y, x = np.mgrid[0:h, 0:w].astype(np.float32) + .5
    r = h / 2 - inset
    dx = np.maximum(np.maximum(inset + r - x, x - (w - inset - r)), 0)
    return np.clip(r - np.sqrt(dx ** 2 + (y - h / 2) ** 2) + .5, 0, 1).astype(np.float32)

def save(name, rgb, alpha):
    h, w = alpha.shape
    size = (w * SCALE, h * SCALE)
    big = cv2.resize(rgb, size, interpolation=cv2.INTER_LANCZOS4)
    a = np.clip(cv2.GaussianBlur(cv2.resize(alpha, size, interpolation=cv2.INTER_LINEAR), (0, 0), 1.0), 0, 1)
    Image.fromarray(np.dstack([big, (a * 255).astype(np.uint8)])).save(OUT / (name + ".png"))
    print(name, size)

def solid(name, x0, y0, x1, y1):
    crop = src[y0:y1, x0:x1]
    save(name, crop, rim_mask(crop))

def stretchable(name, x0, y0, x1, y1, left, right, column):
    """A pill with its contents removed: both end caps around a single clean column."""
    crop = src[y0:y1, x0:x1]
    mask = rim_mask(crop)
    mid = 12
    rgb = np.hstack([crop[:, :left], np.repeat(crop[:, column:column + 1], mid, 1), crop[:, -right:]])
    a = np.hstack([mask[:, :left], np.repeat(mask[:, column:column + 1], mid, 1), mask[:, -right:]])
    save(name, rgb, a)

def keyed(name, x0, y0, x1, y1, low, high, color=None):
    """Light lettering on the dark sky: coverage from the red channel, colour flattened or kept."""
    crop = src[y0:y1, x0:x1]
    a = np.clip((crop[..., 0].astype(np.float32) - low) / (high - low), 0, 1)
    rgb = np.full_like(crop, 0); rgb[...] = color if color else crop
    if not color:
        un = np.clip(crop.astype(np.float32) / np.maximum(a[..., None], .35), 0, 255).astype(np.uint8)
        rgb = un
    save(name, rgb, a)

OUT.mkdir(parents=True, exist_ok=True)
keyed("jetski-runner-title", 24, 190, 842, 336, 85, 205, (255, 247, 228))
keyed("pirate-escape", 62, 336, 790, 378, 75, 190)
keyed("rekor", 258, 420, 446, 470, 80, 200, (255, 247, 228))
solid("oyna", 74, 1392, 776, 1578)
solid("gelistir", 61, 1598, 418, 1715)
solid("ayarlar", 434, 1598, 791, 1715)
crop = src[66:160, 22:116]; save("sound", crop, stadium(94, 94, 1.0))
stretchable("pill-record", 204, 401, 650, 490, 54, 54, 56)
crop = src[72:154, 575:833]; mask = stadium(82, 258, 1.0)
rgb = np.hstack([crop[:, :96], np.repeat(crop[:, 104:105], 12, 1), crop[:, -24:]])
save("pill-coins", rgb, np.hstack([mask[:, :96], np.repeat(mask[:, 104:105], 12, 1), mask[:, -24:]]))
