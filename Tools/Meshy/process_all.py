#!/usr/bin/env python3
"""Runs process_asset.py on every raw Meshy GLB (no credits): game GLBs to Assets, review renders to Logs."""
import json, subprocess, sys
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
args = sys.argv[1:]
manifest = "shore_assets.json"
if args and args[0].endswith(".json"): manifest = args.pop(0)
ASSETS = json.loads((Path(__file__).parent / manifest).read_text())
only = set(args)

def run(x):
    tex = 2048 if x["size"] >= 4 else 1024         # small props never fill the screen
    r = subprocess.run(["blender", "-b", "-P", str(ROOT / "Tools/Meshy/process_asset.py"), "--",
                        str(ROOT / "output/meshy_raw" / (x["name"] + ".glb")), str(ROOT / "Assets/Models/Meshy/Shore" / (x["name"] + ".glb")),
                        "--fit", x["fit"], "--size", str(x["size"]), "--faces", str(x["faces"]), "--texture", str(tex), "--voxel", str(x.get("voxel", 0)), "--textures-dir", str(ROOT / "Assets/Art/Shore"),
                        "--renders", str(ROOT / "Logs/ShoreRenders" / x["name"])], capture_output=True, text=True)
    line = [l for l in r.stdout.splitlines() if l.startswith("PROCESS ")]
    if not line: return x["name"], "FAILED: " + (r.stdout + r.stderr)[-400:]
    d = json.loads(line[0][8:])
    for suffix in ("_color", "_normal"):            # JPEG sources keep the public repo small; Unity recompresses anyway
        png = ROOT / "Assets/Art/Shore" / (x["name"] + suffix + ".png")
        Image.open(png).convert("RGB").save(png.with_suffix(".jpg"), quality=93, subsampling=0); png.unlink()
    return x["name"], f'{d["raw_faces"]} -> {d["faces"]} faces, welded {d["welded_verts"]}, {d["dims_m"]} m'

with ThreadPoolExecutor(3) as ex:
    for name, msg in ex.map(run, [x for x in ASSETS if not only or x["name"] in only]):
        print(name, msg, flush=True)
