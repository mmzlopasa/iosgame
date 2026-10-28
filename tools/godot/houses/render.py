"""A tiny software renderer to look at the kit's buildings without Godot or Blender: orthographic view,
textured with the atlas, vertex colours, sun with a shadow map, sky ambient and a painted outline.
Used for previews and for the build-menu pictures (art/ui/Buildings/<sprite>.png)."""
import math

import numpy as np
from PIL import Image, ImageFilter

from kit import norm


def look(yaw, pitch):
    """Camera rotation: rows are the camera's right, up and back axes in world space."""
    y, p = math.radians(yaw), math.radians(pitch)
    back = np.array([math.sin(y) * math.cos(p), math.sin(p), -math.cos(y) * math.cos(p)])
    right = norm(np.cross(np.array([0, 1, 0.0]), back))
    up = np.cross(back, right)
    return np.stack([right, up, back])


def raster(P2, Z, I, W, H, attrs=None):
    """Z-buffered triangles. P2: pixel xy, Z: depth (bigger = nearer). Returns depth, tri id, barycentrics."""
    depth = np.full((H, W), -np.inf)
    tri = np.full((H, W), -1, dtype=np.int64)
    bary = np.zeros((H, W, 3))
    T = I.reshape(-1, 3)
    for t, (a, b, c) in enumerate(T):
        pa, pb, pc = P2[a], P2[b], P2[c]
        x0 = max(int(math.floor(min(pa[0], pb[0], pc[0]))), 0)
        x1 = min(int(math.ceil(max(pa[0], pb[0], pc[0]))), W - 1)
        y0 = max(int(math.floor(min(pa[1], pb[1], pc[1]))), 0)
        y1 = min(int(math.ceil(max(pa[1], pb[1], pc[1]))), H - 1)
        if x1 < x0 or y1 < y0:
            continue
        den = (pb[1] - pc[1]) * (pa[0] - pc[0]) + (pc[0] - pb[0]) * (pa[1] - pc[1])
        if abs(den) < 1e-12:
            continue
        xs = np.arange(x0, x1 + 1) + 0.5
        ys = np.arange(y0, y1 + 1) + 0.5
        X, Y = np.meshgrid(xs, ys)
        w0 = ((pb[1] - pc[1]) * (X - pc[0]) + (pc[0] - pb[0]) * (Y - pc[1])) / den
        w1 = ((pc[1] - pa[1]) * (X - pc[0]) + (pa[0] - pc[0]) * (Y - pc[1])) / den
        w2 = 1 - w0 - w1
        inside = (w0 >= -1e-6) & (w1 >= -1e-6) & (w2 >= -1e-6)
        if not inside.any():
            continue
        z = w0 * Z[a] + w1 * Z[b] + w2 * Z[c]
        sub = depth[y0:y1 + 1, x0:x1 + 1]
        win = inside & (z > sub)
        if not win.any():
            continue
        sub[win] = z[win]
        tri[y0:y1 + 1, x0:x1 + 1][win] = t
        bb = bary[y0:y1 + 1, x0:x1 + 1]
        bb[win] = np.stack([w0[win], w1[win], w2[win]], axis=1)
    return depth, tri, bary


def render(mesh, atlas, size=512, yaw=-35, pitch=32, sun=(-40, 50), pad=0.08, ground=True, bg=(0, 0, 0, 0),
           outline=(58, 34, 20), outline_px=3, extent=None):
    P, Nn, UV, C, I = mesh.P, mesh.N, mesh.UV, mesh.C, mesh.I
    if ground:
        lo, hi = P.min(axis=0), P.max(axis=0)
        r = max(hi[0] - lo[0], hi[2] - lo[2]) * 0.75
        gp = np.array([[-r, 0, -r], [r, 0, -r], [r, 0, r], [-r, 0, r]], float)
    S = 2  # supersample
    W = H = size * S
    cam = look(yaw, pitch)
    V = P @ cam.T
    if extent is None:
        lo2, hi2 = V[:, :2].min(axis=0), V[:, :2].max(axis=0)
        span = max(hi2 - lo2) * (1 + 2 * pad)
        mid = (lo2 + hi2) / 2
    else:
        span, mid = extent
    sc = W / span
    P2 = np.stack([(V[:, 0] - mid[0]) * sc + W / 2, H / 2 - (V[:, 1] - mid[1]) * sc], axis=1)
    depth, tri, bary = raster(P2, V[:, 2], I, W, H)

    # sun shadow map
    sy, sp = sun
    L = look(sy, sp)
    sundir = L[2]
    SV = P @ L.T
    slo, shi = SV[:, :2].min(axis=0), SV[:, :2].max(axis=0)
    SM = 1024
    ssc = SM / max(shi - slo) / 1.05
    SP2 = np.stack([(SV[:, 0] - slo[0]) * ssc + 10, SM - 10 - (SV[:, 1] - slo[1]) * ssc], axis=1)
    sdepth, _, _ = raster(SP2, SV[:, 2], I, SM + 20, SM + 20)

    T = I.reshape(-1, 3)
    mask = tri >= 0
    tt = tri[mask]
    b = bary[mask]
    ids = T[tt]

    def interp(A):
        return (A[ids] * b[:, :, None]).sum(axis=1)

    uv = interp(UV)
    col = interp(C)
    nrm = norm(interp(Nn))
    pos = interp(P)
    # flip normals that face away from the camera (two-sided parts)
    facing = nrm @ cam[2]
    nrm[facing < 0] *= -1
    ah, aw = atlas.shape[:2]
    tx = np.clip((uv[:, 0] * aw).astype(int), 0, aw - 1)
    ty = np.clip(((1 - uv[:, 1]) * ah).astype(int), 0, ah - 1)
    alb = atlas[ty, tx, :3] / 255.0
    alb = alb ** 2.2 * col[:, :3]
    # shadow test
    sv = pos @ L.T
    sx = np.clip(((sv[:, 0] - slo[0]) * ssc + 10).astype(int), 0, SM + 19)
    sy_ = np.clip((SM - 10 - (sv[:, 1] - slo[1]) * ssc).astype(int), 0, SM + 19)
    lit = sv[:, 2] >= sdepth[sy_, sx] - 0.03
    nl = np.clip(nrm @ sundir, 0, 1) * lit
    sky = 0.42 + 0.22 * np.clip(nrm[:, 1], -1, 1)
    sunc = np.array([1.0, 0.93, 0.8]) * 2.3
    skyc = np.array([0.62, 0.72, 0.9])
    rgb = alb * (nl[:, None] * sunc + sky[:, None] * skyc)
    # filmic-ish tonemap
    rgb = rgb / (1 + rgb * 0.35)
    rgb = np.clip(rgb * 1.2, 0, 1) ** (1 / 2.2)
    img = np.zeros((H, W, 4))
    img[..., :3] = bg[:3]
    img[..., 3] = bg[3] if len(bg) > 3 else 255
    img[..., :3] /= 255.0
    img[..., 3] /= 255.0
    img[mask, :3] = rgb
    img[mask, 3] = 1
    out = Image.fromarray((np.clip(img, 0, 1) * 255).astype(np.uint8), "RGBA")
    if outline:
        a = out.split()[3].filter(ImageFilter.MaxFilter(outline_px * S * 2 + 1))
        o = Image.new("RGBA", out.size, outline + (255,))
        o.putalpha(a)
        o.alpha_composite(out)
        out = o
    return out.resize((size, size), Image.LANCZOS)
