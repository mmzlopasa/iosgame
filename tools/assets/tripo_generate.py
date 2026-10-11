#!/usr/bin/env python3
"""Generates game models with Tripo (text → 3D), then prepares them like every other model:
decimated FBX + PBR textures + an entry in art_manifest.json (models "ai_*").

Needs the TRIPO_API_KEY environment variable (never commit it; in CI it is a GitHub secret).
Usage: python3 tools/assets/tripo_generate.py [--only ai_shelter,ai_granary] [--force] [--tris 9000]
"""
import argparse
import json
import os
import subprocess
import sys
import time
import urllib.request

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import build_assets as ba  # noqa: E402

API = "https://api.tripo3d.ai/v2/openapi"
HERE = os.path.dirname(os.path.abspath(__file__))


def call(method, path, key, body=None):
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(API + path, data=data, method=method,
                                 headers={"Authorization": f"Bearer {key}", "Content-Type": "application/json"})
    with urllib.request.urlopen(req, timeout=120) as r:
        out = json.load(r)
    if out.get("code", 0) != 0:
        raise RuntimeError(f"Tripo error: {out}")
    return out["data"]


def generate(key, prompt):
    task = call("POST", "/task", key, {"type": "text_to_model", "prompt": prompt, "texture": True, "pbr": True})
    tid = task["task_id"]
    for _ in range(240):
        time.sleep(5)
        t = call("GET", f"/task/{tid}", key)
        status = t.get("status")
        if status == "success":
            out = t.get("output", {})
            url = out.get("pbr_model") or out.get("model") or out.get("base_model")
            if not url:
                raise RuntimeError(f"no model url in {out}")
            return url
        if status in ("failed", "cancelled", "banned", "expired", "unknown"):
            raise RuntimeError(f"task {tid} {status}")
    raise RuntimeError(f"task {tid} timed out")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--only", default="")
    ap.add_argument("--force", action="store_true")
    ap.add_argument("--tris", type=int, default=9000)
    args = ap.parse_args()
    key = os.environ.get("TRIPO_API_KEY")
    if not key:
        sys.exit("TRIPO_API_KEY is not set")
    spec = json.load(open(os.path.join(HERE, "tripo_models.json")))
    only = set(filter(None, args.only.split(",")))
    manifest = json.load(open(ba.MANIFEST))
    for m in spec["models"]:
        mid = m["id"]
        if only and mid not in only:
            continue
        if mid in manifest["models"] and not args.force:
            print(f"skip {mid} (already generated)")
            continue
        cache = os.path.join(ba.CACHE, "tripo", mid)
        os.makedirs(cache, exist_ok=True)
        glb = os.path.join(cache, f"{mid}.glb")
        if not os.path.exists(glb) or args.force:
            print(f"generating {mid} …")
            url = generate(key, spec["style"] + " " + m["prompt"])
            urllib.request.urlretrieve(url, glb)
        gltf = os.path.join(cache, f"{mid}.gltf")
        subprocess.run(["blender", "-b", "--factory-startup", "-P", os.path.join(HERE, "blender_glb2gltf.py"), "--", glb, gltf],
                       check=True, stdout=subprocess.DEVNULL)
        ba.build_model_from_gltf(mid, gltf, cache, args.tris, "Tripo AI (generated for Founders)", manifest)
        manifest["models"][mid]["height"] = m["height"]
        with open(ba.MANIFEST, "w") as f:
            json.dump(manifest, f, indent=1, sort_keys=True)
    print("done")


if __name__ == "__main__":
    main()
