#!/usr/bin/env python3
"""Generate the shore assets with Meshy Image to 3D, without wasting credits.

Without --go it is a dry run: it prints the plan and cost and makes NO network request.
Safeguards: an explicit asset list is required, --go needs --budget, the balance is checked first, and the
task id is saved straight after each create call. A rerun resumes those tasks instead of paying again.
A failed task is never retried automatically, and the run stops once the failures reach --max-failures.

  python3 Tools/Meshy/generate_shore.py --only 27                     # plan only
  python3 Tools/Meshy/generate_shore.py --only 27 --go --budget 30    # asks for the key without showing it
"""
import argparse, base64, getpass, io, json, os, sys, time, urllib.error, urllib.request
from pathlib import Path
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
COST = 30                       # meshy-7.1 + texture (2K and 4K cost the same; pricing page, Nov 2026)
TERMINAL_BAD = ("FAILED", "CANCELED", "EXPIRED")


def padded_data_uri(path, pad=.1):
    """Adds neutral margin around the image so thin tips are not cropped, returned as a PNG data URI."""
    im = Image.open(path).convert("RGB")
    w, h = im.size
    px, py = int(w * pad), int(h * pad)
    canvas = Image.new("RGB", (w + 2 * px, h + 2 * py), im.getpixel((3, 3)))
    canvas.paste(im, (px, py))
    buf = io.BytesIO(); canvas.save(buf, "PNG")
    return "data:image/png;base64," + base64.b64encode(buf.getvalue()).decode()


class Api:
    def __init__(self, base, key): self.base, self.key = base.rstrip("/"), key

    def call(self, path, body=None):
        req = urllib.request.Request(self.base + path, method="POST" if body is not None else "GET",
                                     data=json.dumps(body).encode() if body is not None else None,
                                     headers={"Authorization": "Bearer " + self.key, "Content-Type": "application/json"})
        with urllib.request.urlopen(req, timeout=120) as r:
            return json.load(r)


def save_state(path, state):
    tmp = Path(str(path) + ".tmp")
    tmp.write_text(json.dumps(state, indent=1))
    tmp.replace(path)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--manifest", default="shore_assets.json")
    ap.add_argument("--only", help="comma-separated asset ids, e.g. 27 or 1,2,3")
    ap.add_argument("--all", action="store_true")
    ap.add_argument("--go", action="store_true", help="really call Meshy and spend credits")
    ap.add_argument("--budget", type=int, help="most credits this run may spend; required with --go")
    ap.add_argument("--parallel", type=int, default=3)
    ap.add_argument("--max-failures", type=int, default=2)
    ap.add_argument("--images", default=str(ROOT / "output/meshy-shore-assets"))
    ap.add_argument("--out", default=str(ROOT / "output/meshy_raw"))   # raw meshes are 30+ MB; process_asset.py makes the game copies
    ap.add_argument("--state", default=str(ROOT / "output/meshy_shore_state.json"))
    a = ap.parse_args()
    if bool(a.only) == a.all: sys.exit("Choose exactly one of --only or --all.")

    assets = json.loads((Path(__file__).parent / a.manifest).read_text())
    ids = {int(x) for x in a.only.split(",")} if a.only else {x["id"] for x in assets}
    unknown = ids - {x["id"] for x in assets}
    if unknown: sys.exit(f"Unknown asset ids: {sorted(unknown)}")
    chosen = [x for x in assets if x["id"] in ids]
    for x in chosen:
        if not (Path(a.images) / (x["name"] + ".png")).is_file(): sys.exit("Missing image: " + x["name"])

    state_path = Path(a.state)
    state = json.loads(state_path.read_text()) if state_path.exists() else {}
    out = Path(a.out)
    todo = [x for x in chosen if x["name"] not in state]      # anything in the state was already paid for
    cost = len(todo) * COST
    print(f"{len(chosen)} chosen, {len(chosen) - len(todo)} already started, {len(todo)} new -> {cost} credits")
    for x in todo: print(f"  {x['name']}  {x['fit']} {x['size']} m, target {x['faces']} faces")
    if not a.go:
        print("Dry run: nothing was sent to Meshy."); return
    if a.budget is None: sys.exit("--go needs --budget.")
    if cost > a.budget: sys.exit(f"Refusing: {cost} credits is over the budget of {a.budget}.")
    key = os.environ.get("MESHY_API_KEY")
    if not key and sys.stdin.isatty(): key = getpass.getpass("Meshy API key (hidden): ").strip()
    if not key: sys.exit("No key: set MESHY_API_KEY or run this in a terminal to be asked for it.")
    api = Api(os.environ.get("MESHY_BASE_URL", "https://api.meshy.ai"), key)

    balance = api.call("/openapi/v1/balance").get("balance")
    print(f"Balance: {balance} credits")
    if cost and (balance is None or balance < cost): sys.exit(f"Refusing: balance {balance} is below the {cost} needed.")

    out.mkdir(parents=True, exist_ok=True)
    failures = spent = 0
    while True:
        active = [x for x in chosen if state.get(x["name"], {}).get("status") in ("PENDING", "IN_PROGRESS")]
        waiting = [x for x in chosen if x["name"] not in state]
        if not active and not waiting: break
        if failures >= a.max_failures: waiting = []                # stop paying; let running tasks finish
        if not active and not waiting: break
        for x in waiting[:max(0, a.parallel - len(active))]:
            if spent + COST > a.budget: sys.exit("Budget reached; stopping.")
            body = {"image_url": padded_data_uri(Path(a.images) / (x["name"] + ".png")), "ai_model": "meshy-7.1",
                    "should_texture": True, "texture_resolution": "4k", "enable_pbr": False,
                    "should_remesh": False, "target_formats": ["glb"]}
            task = api.call("/openapi/v1/image-to-3d", body)["result"]
            state[x["name"]] = {"task": task, "status": "PENDING"}
            save_state(state_path, state)                          # before anything else can go wrong
            spent += COST
            print("Started", x["name"], flush=True)
        for x in [c for c in chosen if state.get(c["name"], {}).get("status") in ("PENDING", "IN_PROGRESS")]:
            s = state[x["name"]]
            try:
                t = api.call("/openapi/v1/image-to-3d/" + s["task"])
            except (urllib.error.URLError, TimeoutError) as e:
                print("Poll retry", x["name"], e); continue
            s["status"] = t["status"]
            if t["status"] == "SUCCEEDED":
                glb = out / (x["name"] + ".glb"); part = Path(str(glb) + ".part")
                urllib.request.urlretrieve(t["model_urls"]["glb"], part); part.replace(glb)
                s["credits"] = t.get("consumed_credits")
                print(f"Done {x['name']} (credits reported: {s['credits']})", flush=True)
            elif t["status"] in TERMINAL_BAD:
                failures += 1
                s["error"] = (t.get("task_error") or {}).get("message", "")
                print(f"FAILED {x['name']}: {s['error']} (not retried)", flush=True)
            save_state(state_path, state)
        time.sleep(float(os.environ.get("MESHY_POLL_SECONDS", "15")))
    done = [k for k, v in state.items() if v["status"] == "SUCCEEDED"]
    print(f"Finished: {len(done)} succeeded, {failures} failed this run.")


if __name__ == "__main__":
    main()
