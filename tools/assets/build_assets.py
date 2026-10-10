#!/usr/bin/env python3
"""Downloads the CC0 assets listed in assets.json from Poly Haven and prepares them for iPhone:

- textures  -> <Name>_albedo.jpg, <Name>_normal.jpg, <Name>_mask.png (R metal, G AO, A smoothness)
- models    -> decimated FBX (Blender) + per-material textures in the same layout
- impostors -> photoreal tree billboards rendered with Blender Cycles (cross quads)
- hdri      -> Sky.hdr

Writes everything into unity/Assets/Art and a manifest the Unity build script reads
(unity/Assets/Art/Resources/art_manifest.json). Downloads are cached in tools/assets/.cache.

Usage: python3 tools/assets/build_assets.py [--only id1,id2] [--skip-impostors]
Needs: Pillow, numpy, Blender 4.2 on PATH (`blender`).
"""
import argparse
import json
import os
import subprocess
import sys
import urllib.request

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
CACHE = os.path.join(HERE, ".cache")
ART = os.path.join(ROOT, "unity", "Assets", "Art")
OUT_TEX = os.path.join(ART, "Textures")
OUT_MODELS = os.path.join(ART, "Resources", "Models")
OUT_SKY = os.path.join(ART, "Sky")
MANIFEST = os.path.join(ART, "Resources", "art_manifest.json")
API = "https://api.polyhaven.com"
UA = {"User-Agent": "FoundersAssetPipeline/1.0"}


def get_json(url):
    with urllib.request.urlopen(urllib.request.Request(url, headers=UA), timeout=120) as r:
        return json.load(r)


def download(url, path):
    if os.path.exists(path) and os.path.getsize(path) > 0:
        return path
    os.makedirs(os.path.dirname(path), exist_ok=True)
    tmp = path + ".part"
    with urllib.request.urlopen(urllib.request.Request(url, headers=UA), timeout=600) as r, open(tmp, "wb") as f:
        while True:
            chunk = r.read(1 << 20)
            if not chunk:
                break
            f.write(chunk)
    os.replace(tmp, path)
    return path


# MARK: - Texture conversion

def save_albedo(src, dst, size):
    img = Image.open(src).convert("RGB")
    if img.width > size:
        img = img.resize((size, size * img.height // img.width), Image.LANCZOS)
    img.save(dst, quality=90, optimize=True)


def save_albedo_alpha(src, alpha_src, dst, size):
    img = Image.open(src).convert("RGBA")
    if alpha_src:
        a = Image.open(alpha_src).convert("L").resize(img.size)
        img.putalpha(a)
    if img.width > size:
        img = img.resize((size, size * img.height // img.width), Image.LANCZOS)
    img.save(dst, optimize=True)


def save_normal(src, dst, size):
    img = Image.open(src).convert("RGB")
    if img.width > size:
        img = img.resize((size, size * img.height // img.width), Image.LANCZOS)
    img.save(dst, quality=95, optimize=True)


def save_mask(arm_src, dst, size, rough_src=None, ao_src=None):
    """URP Lit mask: R = metallic, G = occlusion, A = smoothness (1 - roughness)."""
    if arm_src:
        arm = np.asarray(Image.open(arm_src).convert("RGB")).astype(np.float32)
        ao, rough, metal = arm[..., 0], arm[..., 1], arm[..., 2]
    else:
        rough = np.asarray(Image.open(rough_src).convert("L")).astype(np.float32)
        ao = np.asarray(Image.open(ao_src).convert("L").resize((rough.shape[1], rough.shape[0]))).astype(np.float32) if ao_src else np.full_like(rough, 255)
        metal = np.zeros_like(rough)
    out = np.stack([metal, ao, np.zeros_like(ao), 255 - rough], axis=-1).astype(np.uint8)
    img = Image.fromarray(out, "RGBA")
    if img.width > size:
        img = img.resize((size, size * img.height // img.width), Image.LANCZOS)
    img.save(dst, optimize=True)


def pick(files, *keys, res="1k", fmt=("jpg", "png")):
    for k in keys:
        entry = files.get(k, {}).get(res, {})
        for f in fmt:
            if f in entry:
                return entry[f]["url"]
    return None


def build_texture(spec, manifest):
    tid, name, res = spec["id"], spec["name"], spec.get("res", "1k")
    files = get_json(f"{API}/files/{tid}")
    cache = os.path.join(CACHE, "textures", tid)
    diff = download(pick(files, "Diffuse", res=res), os.path.join(cache, "diff.jpg"))
    nor = download(pick(files, "nor_gl", res=res), os.path.join(cache, "nor.jpg"))
    arm_url = pick(files, "arm", res=res)
    arm = download(arm_url, os.path.join(cache, "arm.jpg")) if arm_url else None
    rough = None if arm else download(pick(files, "Rough", res=res), os.path.join(cache, "rough.jpg"))
    os.makedirs(OUT_TEX, exist_ok=True)
    size = 1024
    save_albedo(diff, os.path.join(OUT_TEX, f"{name}_albedo.jpg"), size)
    save_normal(nor, os.path.join(OUT_TEX, f"{name}_normal.jpg"), size)
    save_mask(arm, os.path.join(OUT_TEX, f"{name}_mask.png"), 512, rough_src=rough)
    manifest["textures"][name] = {
        "source": f"https://polyhaven.com/a/{tid}",
        "albedo": f"Art/Textures/{name}_albedo.jpg",
        "normal": f"Art/Textures/{name}_normal.jpg",
        "mask": f"Art/Textures/{name}_mask.png",
    }
    print(f"texture {name} <- {tid}")


# MARK: - Models

def build_model(spec, manifest):
    mid, target = spec["id"], spec["tris"]
    files = get_json(f"{API}/files/{mid}")
    g = files["gltf"]["1k"]["gltf"]
    cache = os.path.join(CACHE, "models", mid)
    gltf_path = download(g["url"], os.path.join(cache, os.path.basename(g["url"])))
    for rel, inc in g["include"].items():
        download(inc["url"], os.path.join(cache, rel))

    os.makedirs(OUT_MODELS, exist_ok=True)
    fbx_out = os.path.join(OUT_MODELS, f"{mid}.fbx")
    info_out = os.path.join(cache, "blender_info.json")
    cmd = ["blender", "-b", "--factory-startup", "-P", os.path.join(HERE, "blender_decimate.py"), "--",
           gltf_path, fbx_out, str(target), info_out]
    subprocess.run(cmd, check=True, stdout=subprocess.DEVNULL)
    info = json.load(open(info_out))

    # Materials from the glTF itself (texture roles are explicit there).
    doc = json.load(open(gltf_path))
    images = [img.get("uri") for img in doc.get("images", [])]
    tex_img = [t.get("source") for t in doc.get("textures", [])]

    def tex_path(ref):
        if ref is None:
            return None
        uri = images[tex_img[ref["index"]]]
        return os.path.join(cache, uri)

    mats = {}
    os.makedirs(OUT_TEX, exist_ok=True)
    for i, m in enumerate(doc.get("materials", [])):
        mname = m.get("name", f"mat{i}")
        if mname not in info["materials"]:
            continue
        pbr = m.get("pbrMetallicRoughness", {})
        base = tex_path(pbr.get("baseColorTexture"))
        normal = tex_path(m.get("normalTexture"))
        arm = tex_path(pbr.get("metallicRoughnessTexture"))
        alpha = m.get("alphaMode", "OPAQUE") != "OPAQUE"
        key = f"{mid}__{mname}".replace(" ", "_").replace(".", "_")
        entry = {"alphaClip": alpha, "doubleSided": bool(m.get("doubleSided", False))}
        if base:
            dst = os.path.join(OUT_TEX, f"{key}_albedo.{'png' if alpha else 'jpg'}")
            (save_albedo_alpha(base, None, dst, 1024) if alpha else save_albedo(base, dst, 1024))
            entry["albedo"] = "Art/Textures/" + os.path.basename(dst)
        if normal:
            save_normal(normal, os.path.join(OUT_TEX, f"{key}_normal.jpg"), 1024)
            entry["normal"] = f"Art/Textures/{key}_normal.jpg"
        if arm:
            save_mask(arm, os.path.join(OUT_TEX, f"{key}_mask.png"), 512)
            entry["mask"] = f"Art/Textures/{key}_mask.png"
        mats[mname] = entry

    manifest["models"][mid] = {
        "source": f"https://polyhaven.com/a/{mid}",
        "resource": f"Models/{mid}",
        "triangles": info["triangles"],
        "size": info["size"],
        "materials": mats,
        "materialOrder": info["materials"],
    }
    print(f"model {mid}: {info['source_triangles']} -> {info['triangles']} tris, size {info['size']}")


def build_impostor(spec, manifest):
    mid, name = spec["id"], spec["name"]
    files = get_json(f"{API}/files/{mid}")
    g = files["gltf"]["1k"]["gltf"]
    cache = os.path.join(CACHE, "models", mid)
    gltf_path = download(g["url"], os.path.join(cache, os.path.basename(g["url"])))
    for rel, inc in g["include"].items():
        download(inc["url"], os.path.join(cache, rel))
    os.makedirs(OUT_TEX, exist_ok=True)
    info_out = os.path.join(cache, f"impostor_info_{name}.json")
    cmd = ["blender", "-b", "--factory-startup", "-P", os.path.join(HERE, "blender_impostor.py"), "--",
           gltf_path, os.path.join(OUT_TEX, f"{name}_impostor"), info_out]
    if spec.get("node"):
        cmd.append(spec["node"])
    subprocess.run(cmd, check=True, stdout=subprocess.DEVNULL)
    info = json.load(open(info_out))
    manifest["impostors"][name] = {
        "source": f"https://polyhaven.com/a/{mid}",
        "side": f"Art/Textures/{name}_impostor_side.png",
        "side2": f"Art/Textures/{name}_impostor_side2.png",
        "top": f"Art/Textures/{name}_impostor_top.png",
        "height": info["height"],
        "width": info["width"],
    }
    print(f"impostor {name} <- {mid}: {info}")


def build_hdri(spec, manifest):
    files = get_json(f"{API}/files/{spec['id']}")
    url = files["hdri"][spec.get("res", "2k")]["hdr"]["url"]
    os.makedirs(OUT_SKY, exist_ok=True)
    download(url, os.path.join(OUT_SKY, "Sky.hdr"))
    manifest["sky"] = {"source": f"https://polyhaven.com/a/{spec['id']}", "hdr": "Art/Sky/Sky.hdr"}
    print(f"hdri {spec['id']}")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--only", default="")
    ap.add_argument("--skip-impostors", action="store_true")
    args = ap.parse_args()
    only = set(filter(None, args.only.split(",")))
    spec = json.load(open(os.path.join(HERE, "assets.json")))
    manifest = {"license": "CC0 1.0 (Poly Haven)", "textures": {}, "models": {}, "impostors": {}, "sky": None}
    if os.path.exists(MANIFEST):
        manifest.update({k: v for k, v in json.load(open(MANIFEST)).items() if k in manifest})

    def want(i):
        return not only or i in only

    if want(spec["hdri"]["id"]):
        build_hdri(spec["hdri"], manifest)
    for t in spec["textures"]:
        if want(t["id"]):
            build_texture(t, manifest)
    for m in spec["models"]:
        if want(m["id"]):
            build_model(m, manifest)
    if not args.skip_impostors:
        for t in spec["impostors"]:
            if want(t["id"]):
                build_impostor(t, manifest)

    os.makedirs(os.path.dirname(MANIFEST), exist_ok=True)
    with open(MANIFEST, "w") as f:
        json.dump(manifest, f, indent=1, sort_keys=True)
    print("manifest:", MANIFEST)


if __name__ == "__main__":
    sys.exit(main())
