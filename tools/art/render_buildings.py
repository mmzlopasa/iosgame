#!/usr/bin/env python3
"""Renders the build-menu / building-card pictures of every building and decoration.

Two sources:
  --models <building-models.zip | dir>   the Unity export (CI workflow "Export building models (OBJ)",
                                         artifact "building-models"): every model exactly as the game
                                         assembles it. This is the real pipeline.
  --fbx                                  local fallback without Unity: the buildings that are a single
                                         kit FBX (medieval_*), placed as BuildingModels.Medieval does.

Steps: write a job list -> Blender (blender_buildings.py, Cycles) raw 512 px renders + shadow passes ->
finish (kit outline #2B1A0E from post_icons.py, soft ground shadow, light grade, premultiplied
downsample to 256 px) -> unity/Assets/Art/UI/Resources/UI/Buildings/<key with ':' as '_'>.png,
which Thumbnails.Get prefers over its runtime renders. Optionally a labelled contact sheet.

Usage:
  python3 tools/art/render_buildings.py --models ~/Downloads/building-models.zip
  python3 tools/art/render_buildings.py --fbx [--only medieval_house,medieval_mill]
  options: --samples N (192)  --res PX (512)  --out DIR  --raw DIR  --sheet out.png  --no-render (post only)
Needs Blender 4.2 (blender on PATH or /usr/local/bin/blender), Python 3 with pillow and numpy.
"""
import glob
import json
import os
import shutil
import subprocess
import sys
import tempfile
import zipfile

import numpy as np
from PIL import Image, ImageDraw, ImageEnhance, ImageFilter, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
sys.path.insert(0, HERE)
import post_icons  # noqa: E402  (outline dilation, blur, ink colour)

ASSETS = os.path.join(ROOT, "unity", "Assets")
OUT = os.path.join(ASSETS, "Art", "UI", "Resources", "UI", "Buildings")
CACHE = os.path.join(HERE, ".cache", "buildings")
SIZE = 256
OUTLINE = 15.0          # px at 512 (7.5 px at 256, like the runtime thumbnails' 9 px but crisper)
SHADOW_STRENGTH = 0.62  # ground shadow opacity
GRADE_SATURATION = 1.10
GRADE_CONTRAST = 1.03


def arg(flag, default=None):
    return sys.argv[sys.argv.index(flag) + 1] if flag in sys.argv else default


def blender():
    return shutil.which("blender") or "/usr/local/bin/blender"


def file_name(key):
    return key.replace(":", "_")


# ---------------------------------------------------------------------------------------- jobs

def jobs_from_export(src):
    """Job list from the Unity export (zip, nested zip from a GitHub artifact, or folder)."""
    if os.path.isfile(src):
        dst = tempfile.mkdtemp(prefix="building-models-")
        with zipfile.ZipFile(src) as z:
            z.extractall(dst)
        inner = glob.glob(os.path.join(dst, "*.zip"))
        if inner and not glob.glob(os.path.join(dst, "**", "manifest.json"), recursive=True):
            with zipfile.ZipFile(inner[0]) as z:
                z.extractall(dst)
        src = dst
    found = glob.glob(os.path.join(src, "**", "manifest.json"), recursive=True)
    if not found:
        sys.exit(f"no manifest.json under {src}")
    base = os.path.dirname(found[0])
    manifest = json.load(open(found[0]))
    jobs = []
    for m in manifest["models"]:
        mats = {}
        for spec in m["materials"]:
            s = dict(spec)
            for k in ("albedo", "normal", "mask"):
                if s.get(k):
                    s[k] = os.path.join(base, s[k])
            mats[spec["name"]] = s
        jobs.append({"key": m["key"], "file": file_name(m["key"]), "format": "obj",
                     "source": os.path.join(base, m["obj"]), "materials": mats,
                     "effects": m.get("effects", [])})
    return jobs


def wanted():
    """Thumbnails.Wanted: (key, sprite, decoration, footprint metres), first occurrence wins."""
    content = json.load(open(os.path.join(ASSETS, "Resources", "content.json")))
    out, seen = [], set()
    for b in content["buildings"]:
        fp = b.get("footprint", {"width": 1, "height": 1})
        metres = max(fp["width"], fp["height"]) * 2.0
        for s in [b.get("sprite") or b["id"]] + [l["sprite"] for l in b.get("looks", [])]:
            if s not in seen:
                seen.add(s)
                out.append((s, s, False, metres))
    for d in content.get("decorations", []):
        k = "deco:" + (d.get("sprite") or d["id"])
        if k not in seen:
            seen.add(k)
            out.append((k, k[5:], True, 2.0))
    return out


# BuildingModels.Detailed: the fire of a detailed model, in the model's space (Unity), as exporter coordinates.
DETAILED_FIRES = {
    "shelter": ((-1.3, 0.04, 0.4), 0.3), "pottery_kiln": ((-0.45, 0.12, -0.45), 0.4),
    "copper_smelter": ((0.0, 1.3, 0.2), 0.5), "bronze_workshop": ((0.45, 0.64, 0.3), 0.3),
}


def jobs_from_fbx():
    """Local fallback: buildings that are one model straight from the FBX - the medieval kit buildings
    (BuildingModels.Medieval) and the detailed bw_<sprite> buildings (BuildingModels.Detailed)."""
    art = json.load(open(os.path.join(ASSETS, "Art", "Resources", "art_manifest.json")))
    jobs = []
    for key, sprite, deco, metres in wanted():
        detailed = not deco and ("bw_" + sprite) in art["models"]
        if not detailed and (deco or not sprite.startswith("medieval_") or sprite not in art["models"]):
            continue
        model = art["models"]["bw_" + sprite if detailed else sprite]
        native = 6.0 if sprite in ("medieval_market", "medieval_church", "medieval_barn") else 4.0
        k = min(1.0, metres / native)
        mats = {}
        for name, t in model["materials"].items():
            has_mask = bool(t.get("mask"))
            mats[name] = {
                "color": [1, 1, 1, 1],
                "smoothness": 1.0 if has_mask else 0.2,  # ArtSetup.TexturedLit
                "metallic": 0.0,
                "albedo": os.path.join(ASSETS, t["albedo"]) if t.get("albedo") else None,
                "normal": os.path.join(ASSETS, t["normal"]) if t.get("normal") else None,
                "mask": os.path.join(ASSETS, t["mask"]) if has_mask else None,
                "alphaClip": bool(t.get("alphaClip")), "cutoff": 0.45,
                "doubleSided": bool(t.get("doubleSided")),
            }
        effects = []
        if detailed:
            if sprite in DETAILED_FIRES:
                (x, y, z), scale = DETAILED_FIRES[sprite]
                effects.append({"kind": "Flames", "position": [-x, y, z], "size": 0.6 * scale})
            jobs.append({"key": key, "file": file_name(key), "format": "fbx",
                         "source": os.path.join(ASSETS, model["resource"].replace("Models/", "Art/Resources/Models/") + ".fbx"),
                         "transform": {"yaw": 0.0, "scale": 1.0}, "materials": mats, "effects": effects})
            continue
        if sprite == "medieval_smithy":
            # b.Fire(new Vector3(-0.55f, 0.95f, 1.75f), 0.35f) in the model's space (Unity), as exporter coordinates.
            effects.append({"kind": "Flames", "position": [0.55, 0.95, 1.75], "size": 0.6 * 0.35})
        jobs.append({"key": key, "file": file_name(key), "format": "fbx",
                     "source": os.path.join(ASSETS, model["resource"].replace("Models/", "Art/Resources/Models/") + ".fbx"),
                     "transform": {"yaw": 180.0, "scale": 0.94 * k}, "materials": mats, "effects": effects})
    return jobs


# ---------------------------------------------------------------------------------------- finishing

def finish(raw_png, shadow_png, dst, size=SIZE, outline=OUTLINE):
    im = Image.open(raw_png).convert("RGBA")
    n = im.size[0]
    rgb = im.convert("RGB")
    rgb = ImageEnhance.Color(rgb).enhance(GRADE_SATURATION)
    rgb = ImageEnhance.Contrast(rgb).enhance(GRADE_CONTRAST)
    c = np.asarray(rgb, np.float32) / 255
    a = np.asarray(im, np.float32)[..., 3] / 255
    ol = post_icons.dilate(a, outline * n / 512)

    sh = np.zeros_like(a)
    if shadow_png and os.path.exists(shadow_png):
        sh = np.asarray(Image.open(shadow_png).convert("RGBA"), np.float32)[..., 3] / 255
        sh = post_icons.blur(sh, 1.2 * n / 512) * SHADOW_STRENGTH
        # Fade out towards the frame so long shadows never end in a hard edge.
        yy, xx = np.mgrid[0:n, 0:n].astype(np.float32) / (n - 1)
        edge = np.minimum(np.minimum(xx, 1 - xx), np.minimum(yy, 1 - yy))
        sh *= np.clip(edge / 0.07, 0, 1) ** 1.5
        # Keep it a contact shadow: strongest at the model, gone a little way out from it.
        near = post_icons.blur(post_icons.dilate(a, 0.06 * n), 0.05 * n)
        sh *= np.clip(near * 1.4, 0, 1)

    out_c = np.zeros_like(c)
    out_a = np.zeros_like(a)

    def over(col, al):
        nonlocal out_c, out_a
        na = al + out_a * (1 - al)
        safe = np.where(na > 1e-6, na, 1)
        out_c = (col * al[..., None] + out_c * (out_a * (1 - al))[..., None]) / safe[..., None]
        out_a = na

    over(np.zeros_like(c), sh)
    over(np.broadcast_to(post_icons.INK, c.shape), ol)
    over(c, a)

    # Premultiplied Lanczos downsample, then a light unsharp mask on the colour.
    pm = np.dstack([out_c * out_a[..., None], out_a])
    chans = [Image.fromarray(np.clip(pm[..., i] * 255 + 0.5, 0, 255).astype(np.uint8)).resize((size, size), Image.LANCZOS)
             for i in range(4)]
    arr = np.dstack([np.asarray(ch, np.float32) / 255 for ch in chans])
    al = np.clip(arr[..., 3], 0, 1)
    col = np.where(al[..., None] > 1e-4, arr[..., :3] / np.maximum(al[..., None], 1e-4), 0)
    small = Image.fromarray((np.dstack([np.clip(col, 0, 1), al]) * 255 + 0.5).astype(np.uint8), "RGBA")
    rgb_s = small.convert("RGB").filter(ImageFilter.UnsharpMask(radius=0.7, percent=40, threshold=1))
    small = Image.merge("RGBA", (*rgb_s.split(), small.split()[3]))
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    small.save(dst, optimize=True)


def sheet(files, out, cols=4, cell=280, bg=("#E9D8B0", "#3E6FB0")):
    """Contact sheet: each picture on a parchment card and on the kit blue, labelled."""
    font_path = post_icons.FONT
    font = ImageFont.truetype(font_path, 18) if os.path.exists(font_path) else None
    rows = (len(files) + cols - 1) // cols
    W, H = cols * cell * 2, rows * (cell + 26)
    canvas = Image.new("RGB", (W, H), "#2A2420")
    d = ImageDraw.Draw(canvas)
    for i, f in enumerate(files):
        im = Image.open(f).convert("RGBA")
        x0, y0 = (i % cols) * cell * 2, (i // cols) * (cell + 26)
        for j, colr in enumerate(bg):
            x = x0 + j * cell
            d.rounded_rectangle((x + 6, y0 + 6, x + cell - 6, y0 + cell - 6), 18, fill=colr)
            canvas.paste(im, (x + (cell - im.size[0]) // 2, y0 + (cell - im.size[1]) // 2), im)
        name = os.path.basename(f)[:-4]
        tw = d.textlength(name, font=font)
        d.text((x0 + cell - tw / 2, y0 + cell - 2), name, font=font, fill="#FFFFFF")
    canvas.save(out)
    print("sheet", out)


# ---------------------------------------------------------------------------------------- main

def main():
    if "--models" in sys.argv:
        jobs = jobs_from_export(arg("--models"))
    elif "--fbx" in sys.argv:
        jobs = jobs_from_fbx()
    else:
        sys.exit(__doc__)
    only = [k for k in (arg("--only", "") or "").split(",") if k]
    if only:
        jobs = [j for j in jobs if j["key"] in only or j["file"] in only]
    raw = arg("--raw", os.path.join(CACHE, "raw"))
    out = arg("--out", OUT)
    os.makedirs(raw, exist_ok=True)
    jobs_file = os.path.join(raw, "jobs.json")
    with open(jobs_file, "w") as f:
        json.dump(jobs, f, indent=1)
    print(f"{len(jobs)} models: {' '.join(j['key'] for j in jobs)}")

    if "--no-render" not in sys.argv:
        cmd = [blender(), "-b", "--factory-startup", "-P", os.path.join(HERE, "blender_buildings.py"), "--",
               jobs_file, raw, "--samples", arg("--samples", "192"), "--res", arg("--res", "512")]
        proc = subprocess.run(cmd, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
        for line in proc.stdout.splitlines():
            if line.startswith(("[done]", "[fail]", "[skip]", "[warn]", "[hdri]", "Traceback", "  File", "Error")):
                print(line)
        if proc.returncode != 0:
            sys.exit(f"blender failed ({proc.returncode})")

    written = []
    for j in jobs:
        src = os.path.join(raw, j["file"] + ".png")
        if not os.path.exists(src):
            print("missing render:", j["key"])
            continue
        dst = os.path.join(out, j["file"] + ".png")
        finish(src, os.path.join(raw, j["file"] + "_shadow.png"), dst)
        written.append(dst)
        print("picture", dst)
    if arg("--sheet") and written:
        sheet(written, arg("--sheet"))


if __name__ == "__main__":
    main()
