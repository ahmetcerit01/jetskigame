#!/usr/bin/env python3
"""Meshy Text to Image with the same safeguards as generate_shore.py. Without --go it sends nothing.
  python3 Tools/Meshy/generate_images.py ice_images.json --go --budget 28
"""
import argparse, getpass, json, os, sys, time, urllib.error, urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
MODEL, COST = "nano-banana-2-1", 4     # pricing page, Nov 2026

ap = argparse.ArgumentParser()
ap.add_argument("manifest"); ap.add_argument("--only"); ap.add_argument("--go", action="store_true"); ap.add_argument("--budget", type=int)
ap.add_argument("--out", default=str(ROOT / "output/meshy-env-images")); ap.add_argument("--state", default=str(ROOT / "output/meshy_env_images_state.json"))
a = ap.parse_args()
man = json.loads((Path(__file__).parent / a.manifest).read_text())
chosen = [x for x in man["assets"] if not a.only or x["name"][:2] in a.only.split(",")]
state_path = Path(a.state)
state = json.loads(state_path.read_text()) if state_path.exists() else {}
todo = [x for x in chosen if x["name"] not in state]
cost = len(todo) * COST
print(f"{len(chosen)} chosen, {len(todo)} new -> {cost} credits")
for x in todo: print("  " + x["name"])
if not a.go: print("Dry run: nothing was sent to Meshy."); sys.exit()
if a.budget is None: sys.exit("--go needs --budget.")
if cost > a.budget: sys.exit(f"Refusing: {cost} credits is over the budget of {a.budget}.")
key = os.environ.get("MESHY_API_KEY")
if not key and sys.stdin.isatty(): key = getpass.getpass("Meshy API key (hidden): ").strip()
if not key: sys.exit("No key: set MESHY_API_KEY or run this in a terminal to be asked for it.")
base = os.environ.get("MESHY_BASE_URL", "https://api.meshy.ai").rstrip("/")

def call(path, body=None):
    req = urllib.request.Request(base + path, method="POST" if body is not None else "GET", data=json.dumps(body).encode() if body is not None else None,
                                 headers={"Authorization": "Bearer " + key, "Content-Type": "application/json"})
    with urllib.request.urlopen(req, timeout=120) as r: return json.load(r)

def save():
    tmp = Path(str(state_path) + ".tmp"); tmp.write_text(json.dumps(state, indent=1)); tmp.replace(state_path)

balance = call("/openapi/v1/balance").get("balance")
print("Balance:", balance)
if cost and (balance is None or balance < cost): sys.exit(f"Refusing: balance {balance} is below {cost}.")
out = Path(a.out); out.mkdir(parents=True, exist_ok=True)
for x in todo:
    state[x["name"]] = {"task": call("/openapi/v1/text-to-image", {"ai_model": MODEL, "prompt": x["prompt"] + " " + man["style"], "aspect_ratio": x["aspect"]})["result"], "status": "PENDING"}
    save(); print("Started", x["name"], flush=True)
while True:
    active = [x for x in chosen if state[x["name"]]["status"] in ("PENDING", "IN_PROGRESS")]
    if not active: break
    for x in active:
        s = state[x["name"]]
        try: t = call("/openapi/v1/text-to-image/" + s["task"])
        except (urllib.error.URLError, TimeoutError) as e: print("Poll retry", x["name"], e); continue
        s["status"] = t["status"]
        if t["status"] == "SUCCEEDED":
            urllib.request.urlretrieve(t["image_urls"][0], out / (x["name"] + ".png"))
            s["credits"] = t.get("consumed_credits"); print(f"Done {x['name']} (credits reported: {s['credits']})", flush=True)
        elif t["status"] in ("FAILED", "CANCELED"):
            s["error"] = (t.get("task_error") or {}).get("message", ""); print("FAILED", x["name"], s["error"], "(not retried)", flush=True)
        save()
    time.sleep(float(os.environ.get("MESHY_POLL_SECONDS", "6")))
print("Finished:", sum(v["status"] == "SUCCEEDED" for v in state.values()), "succeeded.")
