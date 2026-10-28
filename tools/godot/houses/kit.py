"""A small mesh kit for the Godot buildings (no Blender needed): numpy parts painted with the shared
building atlas (godot/art/textures/bw_atlas_*.png), soft vertex-colour shading baked in, written as .glb.

Space: metres, +Y up, the door (street) side faces -Z, the footprint centred on the origin, ground y = 0.
"""
import json
import math
import random
import struct

import numpy as np

TILES = ["planks", "planks_old", "log", "logend",
         "shingle", "shingle_red", "thatch", "thatch_old",
         "stone", "plaster", "cloth_red", "cloth_ochre",
         "hide", "iron", "wattle", "straw"]
N = 4
MARGIN = 8 / 512
# Metres of the world one tile covers along (u, v).
TILE_METRES = {
    "planks": (1.0, 1.4), "planks_old": (1.0, 1.4), "log": (0.6, 1.6), "logend": (0.3, 0.3),
    "shingle": (1.0, 0.36), "shingle_red": (1.0, 0.36), "thatch": (1.6, 1.0), "thatch_old": (1.6, 1.0),
    "stone": (0.9, 0.9), "plaster": (2.0, 2.0), "cloth_red": (1.4, 1.4), "cloth_ochre": (1.4, 1.4),
    "hide": (1.2, 1.2), "iron": (0.6, 0.6), "wattle": (1.6, 1.0), "straw": (1.0, 1.0),
}


def tile_rect(name):
    i = TILES.index(name)
    row, col = divmod(i, N)
    return col / N, 1 - (row + 1) / N


def v3(*a):
    return np.array(a if len(a) == 3 else a[0], dtype=np.float64)


def norm(v):
    n = np.linalg.norm(v, axis=-1, keepdims=True)
    return v / np.maximum(n, 1e-9)


def rot_y(deg):
    a = math.radians(deg)
    c, s = math.cos(a), math.sin(a)
    return np.array([[c, 0, s], [0, 1, 0], [-s, 0, c]])


def rot_x(deg):
    a = math.radians(deg)
    c, s = math.cos(a), math.sin(a)
    return np.array([[1, 0, 0], [0, c, -s], [0, s, c]])


def rot_z(deg):
    a = math.radians(deg)
    c, s = math.cos(a), math.sin(a)
    return np.array([[c, -s, 0], [s, c, 0], [0, 0, 1]])


def frame(a, b, up=(0, 1, 0)):
    """A rotation whose local Z runs from a to b (local Y as close to `up` as possible)."""
    z = norm(v3(b) - v3(a))
    up = v3(up)
    if abs(np.dot(z, up)) > 0.98:
        up = v3(1, 0, 0)
    x = norm(np.cross(up, z))
    y = np.cross(z, x)
    return np.stack([x, y, z], axis=1)


class Kit:
    def __init__(self, seed=1):
        self.rng = random.Random(seed)
        self.P, self.Nn, self.UV, self.C, self.I = [], [], [], [], []
        self.count = 0
        # Moves every later part (set with place()).
        self.R = np.eye(3)
        self.T = np.zeros(3)

    # MARK: - low level

    def place(self, at=(0, 0, 0), yaw=0.0):
        self.R = rot_y(yaw)
        self.T = v3(at)

    def _emit(self, P, Nm, UV, C, I):
        P = P @ self.R.T + self.T
        Nm = Nm @ self.R.T
        self.P.append(P)
        self.Nn.append(norm(Nm))
        self.UV.append(UV)
        self.C.append(C)
        self.I.append(I + self.count)
        self.count += len(P)

    def _sub(self, tile, along, across):
        tw, th = TILE_METRES[tile]
        sw = min(1.0, max(0.04, across / tw))
        sh = min(1.0, max(0.04, along / th))
        return self.rng.uniform(0, 1 - sw), self.rng.uniform(0, 1 - sh), sw, sh

    @staticmethod
    def _to_atlas(tile, local, sub):
        u0, v0 = tile_rect(tile)
        su, sv, sw, sh = sub
        uu = np.clip(local[:, 0], 0, 1)
        vv = np.clip(local[:, 1], 0, 1)
        u = u0 + (MARGIN + (su + uu * sw) * (1 - 2 * MARGIN)) / N
        v = v0 + (MARGIN + (sv + vv * sh) * (1 - 2 * MARGIN)) / N
        return np.stack([u, v], axis=1)

    def _color(self, n, color, vary):
        c = np.ones((n, 4))
        k = 1 + self.rng.uniform(-vary, vary)
        c[:, :3] = np.array(color[:3]) * k
        return c

    # MARK: - parts

    def box(self, center, size, R=None, tile="planks", color=(1, 1, 1), vary=0.06, grain=1, uv_scale=1.0,
            round_=0.0, seg=3, jitter=0.0):
        """A box (size x, y, z) centred on `center`, turned by R; `grain` = local axis the texture's v runs along.
        round_ > 0 rounds it into a soft pebble-like block (superellipsoid), seg faces per side."""
        R = np.eye(3) if R is None else R
        size = v3(size)
        h = size / 2
        if round_ > 0:
            self._rounded(v3(center), h, R, tile, color, vary, grain, round_, seg, jitter)
            return
        faces = []
        for axis in range(3):
            for sgn in (-1, 1):
                faces.append((axis, sgn))
        Pl, Nl, UVl, Il = [], [], [], []
        base = 0
        sub_cache = {}
        for axis, sgn in faces:
            n = np.zeros(3)
            n[axis] = sgn
            others = [k for k in range(3) if k != axis]
            # v follows the grain when it lies in the face; u is the other in-face axis
            if grain in others:
                va = grain
                ua = others[0] if others[1] == grain else others[1]
            else:
                ua, va = others
            corners = []
            for a, b in ((-1, -1), (1, -1), (1, 1), (-1, 1)):
                p = np.zeros(3)
                p[axis] = sgn * h[axis]
                p[ua] = a * h[ua]
                p[va] = b * h[va]
                corners.append(p)
            corners = np.array(corners)
            # winding: counter-clockwise seen from outside
            e1, e2 = corners[1] - corners[0], corners[2] - corners[0]
            if np.dot(np.cross(e1, e2), n) < 0:
                corners = corners[::-1]
            local = np.stack([(corners[:, ua] + h[ua]) / max(1e-6, size[ua]),
                              (corners[:, va] + h[va]) / max(1e-6, size[va])], axis=1)
            key = (ua, va)
            if key not in sub_cache:
                sub_cache[key] = self._sub(tile, size[va] * uv_scale, size[ua] * uv_scale)
            UVl.append(self._to_atlas(tile, local, sub_cache[key]))
            Pl.append(corners)
            Nl.append(np.tile(n, (4, 1)))
            Il.append(np.array([0, 1, 2, 0, 2, 3]) + base)
            base += 4
        P = np.concatenate(Pl)
        if jitter:
            P = P + np.array([[self.rng.uniform(-jitter, jitter) for _ in range(3)] for _ in range(len(P))])
        P = P @ R.T + v3(center)
        Nm = np.concatenate(Nl) @ R.T
        self._emit(P, Nm, np.concatenate(UVl), self._color(len(P), color, vary), np.concatenate(Il))

    def _rounded(self, c, h, R, tile, color, vary, grain, p, seg, jitter):
        """A superellipsoid block: a cube grid pushed onto |x|^e+|y|^e+|z|^e = 1 (e = 2 / p, p 0..1)."""
        e = max(2.2, 2.0 + 8.0 * (1 - p))
        Pl, Nl, UVl, Il = [], [], [], []
        base = 0
        sub = self._sub(tile, h[grain] * 2, max(h[k] for k in range(3) if k != grain) * 2)
        jit = None
        for axis in range(3):
            for sgn in (-1, 1):
                others = [k for k in range(3) if k != axis]
                g = np.linspace(-1, 1, seg + 1)
                A, B = np.meshgrid(g, g, indexing="ij")
                q = np.zeros((seg + 1, seg + 1, 3))
                q[..., axis] = sgn
                q[..., others[0]] = A
                q[..., others[1]] = B
                q = q.reshape(-1, 3)
                # project onto the superellipsoid along the ray from the centre
                s = (np.abs(q) ** e).sum(axis=1) ** (-1 / e)
                u = q * s[:, None]
                P = u * h
                # normal: gradient of sum |x/h|^e
                grad = np.sign(u) * np.abs(u) ** (e - 1) / h
                Nm = norm(grad)
                ua, va = (others if grain not in others or others[1] == grain else others[::-1])
                local = np.stack([(u[:, ua] + 1) / 2, (u[:, va] + 1) / 2], axis=1)
                idx = []
                for i in range(seg):
                    for j in range(seg):
                        a = i * (seg + 1) + j
                        b, cc, d = a + seg + 1, a + seg + 2, a + 1
                        idx += [a, b, cc, a, cc, d]
                idx = np.array(idx)
                # fix winding
                t = idx[:3]
                if np.dot(np.cross(P[t[1]] - P[t[0]], P[t[2]] - P[t[0]]), Nm[t[0]] + Nm[t[1]] + Nm[t[2]]) < 0:
                    idx = idx.reshape(-1, 3)[:, ::-1].reshape(-1)
                Pl.append(P)
                Nl.append(Nm)
                UVl.append(self._to_atlas(tile, local, sub))
                Il.append(idx + base)
                base += len(P)
        P = np.concatenate(Pl)
        if jitter:
            # same jitter for coincident seam vertices: hash the rounded position
            off = {}
            out = P.copy()
            for i, p in enumerate(P):
                k = tuple(np.round(p, 4))
                if k not in off:
                    off[k] = np.array([self.rng.uniform(-jitter, jitter) for _ in range(3)]) * h * 2
                out[i] = p + off[k]
            P = out
        self._emit(P @ R.T + c, np.concatenate(Nl) @ R.T, np.concatenate(UVl),
                   self._color(len(P), color, vary), np.concatenate(Il))

    def beam(self, a, b, w=0.12, h=None, tile="planks", color=(1, 1, 1), vary=0.08, up=(0, 1, 0)):
        """A squared timber from a to b (grain along it)."""
        h = h or w
        a, b = v3(a), v3(b)
        L = float(np.linalg.norm(b - a))
        self.box((a + b) / 2, (w, h, L), R=frame(a, b, up), tile=tile, color=color, vary=vary, grain=2)

    def cyl(self, a, b, r, sides=10, tile="log", end_tile="logend", color=(1, 1, 1), vary=0.07, r2=None,
            caps=True, bulge=0.0, rings=1, wobble=0.0, uv_around=1.0):
        """A round log / post / barrel from a to b (radius r at a, r2 at b, bulge in the middle)."""
        a, b = v3(a), v3(b)
        r2 = r if r2 is None else r2
        R = frame(a, b)
        L = float(np.linalg.norm(b - a))
        ang = np.linspace(0, 2 * np.pi, sides + 1)
        ts = np.linspace(0, 1, rings + 1)
        sub = self._sub(tile, L, 2 * np.pi * r * uv_around)
        P, Nm, UV = [], [], []
        ph = self.rng.uniform(0, 6.28)
        for t in ts:
            rr = r + (r2 - r) * t + bulge * math.sin(math.pi * t)
            for k, an in enumerate(ang):
                wob = 1 + wobble * math.sin(an * 3 + ph + t * 5)
                x, y = math.cos(an) * rr * wob, math.sin(an) * rr * wob
                P.append((x, y, t * L))
                slope = (r - r2) / max(L, 1e-6)
                Nm.append((math.cos(an), math.sin(an), slope))
                UV.append((k / sides, t))
        P, Nm, UV = np.array(P), norm(np.array(Nm)), np.array(UV)
        idx = []
        W = sides + 1
        for i in range(rings):
            for k in range(sides):
                p0 = i * W + k
                idx += [p0, p0 + 1, p0 + W + 1, p0, p0 + W + 1, p0 + W]
        idx = np.array(idx)
        if np.dot(np.cross(P[idx[1]] - P[idx[0]], P[idx[2]] - P[idx[0]]), Nm[idx[0]]) < 0:
            idx = idx.reshape(-1, 3)[:, ::-1].reshape(-1)
        self._emit(P @ R.T + a, Nm @ R.T, self._to_atlas(tile, UV, sub), self._color(len(P), color, vary), idx)
        if caps:
            for t, rr, s in ((0, r, -1), (1, r2, 1)):
                cp = [(0, 0, t * L)] + [(math.cos(an) * rr, math.sin(an) * rr, t * L) for an in ang[:-1]]
                cp = np.array(cp)
                uv = np.array([(0.5, 0.5)] + [(0.5 + 0.5 * math.cos(an), 0.5 + 0.5 * math.sin(an)) for an in ang[:-1]])
                n = np.tile((0, 0, s), (len(cp), 1)).astype(float)
                ci = []
                for k in range(sides):
                    ci += [0, 1 + k, 1 + (k + 1) % sides]
                ci = np.array(ci)
                if s < 0:
                    ci = ci.reshape(-1, 3)[:, ::-1].reshape(-1)
                self._emit(cp @ R.T + a, n @ R.T, self._to_atlas(end_tile, uv, (0, 0, 1, 1)),
                           self._color(len(cp), color, vary), ci)

    def quad_slab(self, p00, p10, p11, p01, thick, tile, color=(1, 1, 1), vary=0.05, grain_v=True):
        """A thick panel on the quad p00-p10-p11-p01 (u along p00→p10, v along p00→p01), extruded inward."""
        p = [v3(x) for x in (p00, p10, p11, p01)]
        n = norm(np.cross(p[1] - p[0], p[3] - p[0]))
        top = np.array(p)
        bot = top - n * thick
        L_u = float(np.linalg.norm(p[1] - p[0]))
        L_v = float(np.linalg.norm(p[3] - p[0]))
        sub = self._sub(tile, L_v, L_u)
        uv = np.array([(0, 0), (1, 0), (1, 1), (0, 1)], float)
        col = self._color(4, color, vary)
        # top
        self._emit(top, np.tile(n, (4, 1)), self._to_atlas(tile, uv, sub), col, np.array([0, 1, 2, 0, 2, 3]))
        # bottom (darker)
        self._emit(bot[::-1], np.tile(-n, (4, 1)), self._to_atlas(tile, uv, sub), col * [0.7, 0.7, 0.7, 1],
                   np.array([0, 1, 2, 0, 2, 3]))
        # rims
        for i in range(4):
            j = (i + 1) % 4
            q = np.array([top[i], bot[i], bot[j], top[j]])
            e = norm(np.cross(q[1] - q[0], q[3] - q[0]))
            if np.dot(e, (top[i] + top[j]) / 2 - top.mean(axis=0)) < 0:
                q = q[::-1]
                e = -e
            self._emit(q, np.tile(e, (4, 1)), self._to_atlas(tile, uv * [1, thick / max(L_v, 0.01)], sub),
                       col, np.array([0, 1, 2, 0, 2, 3]))

    def tri_panel(self, a, b, c, thick, tile, color=(1, 1, 1), vary=0.05):
        """A thick triangle (gable ends)."""
        p = np.array([v3(a), v3(b), v3(c)])
        n = norm(np.cross(p[1] - p[0], p[2] - p[0]))
        sub = self._sub(tile, 2.0, 2.0)
        lo = p.min(axis=0)
        ext = np.maximum(p.max(axis=0) - lo, 1e-6)
        ax = int(np.argmax(np.abs(n)))
        o = [k for k in range(3) if k != ax]
        uv = np.stack([(p[:, o[0]] - lo[o[0]]) / ext[o[0]], (p[:, 1] - lo[1]) / ext[1]], axis=1)
        col = self._color(3, color, vary)
        self._emit(p, np.tile(n, (3, 1)), self._to_atlas(tile, uv, sub), col, np.array([0, 1, 2]))
        q = p - n * thick
        self._emit(q, np.tile(-n, (3, 1)), self._to_atlas(tile, uv, sub), col, np.array([0, 2, 1]))

    # MARK: - finishing

    def build(self):
        P = np.concatenate(self.P)
        Nm = np.concatenate(self.Nn)
        UV = np.concatenate(self.UV)
        C = np.concatenate(self.C)
        I = np.concatenate(self.I).astype(np.uint32)
        return Mesh(P, Nm, UV, C, I)


class Mesh:
    def __init__(self, P, N, UV, C, I):
        self.P, self.N, self.UV, self.C, self.I = P, N, UV, C, I

    def bake_shading(self, ground_ao=0.35, ao_height=0.6, occluders=None):
        """Painted shading in the vertex colours: darker near the ground and in the eaves' shade,
        a little lighter on top (a soft sky light the renderer's ambient would miss)."""
        y = self.P[:, 1]
        k = 1 - ground_ao * np.clip(1 - y / ao_height, 0, 1) ** 1.6
        k *= 0.92 + 0.12 * np.clip(self.N[:, 1], 0, 1)
        if occluders:
            for fn in occluders:
                k *= fn(self.P, self.N)
        self.C[:, :3] *= k[:, None]
        self.C[:, :3] = np.clip(self.C[:, :3], 0, 1.4)

    def bounds(self):
        return self.P.min(axis=0), self.P.max(axis=0)

    def write_glb(self, path, material="Atlas"):
        P = self.P.astype(np.float32)
        N = self.N.astype(np.float32)
        UV = self.UV.astype(np.float32).copy()
        UV[:, 1] = 1 - UV[:, 1]  # glTF v runs down the image
        C = np.clip(self.C, 0, 1).astype(np.float32)
        I = self.I.astype(np.uint32)
        blobs = [P.tobytes(), N.tobytes(), UV.tobytes(), C.tobytes(), I.tobytes()]
        offs, buf = [], b""
        for bl in blobs:
            offs.append(len(buf))
            buf += bl
            buf += b"\0" * ((4 - len(buf) % 4) % 4)
        lo, hi = P.min(axis=0), P.max(axis=0)
        views = [{"buffer": 0, "byteOffset": offs[i], "byteLength": len(blobs[i]),
                  "target": 34963 if i == 4 else 34962} for i in range(5)]
        acc = [
            {"bufferView": 0, "componentType": 5126, "count": len(P), "type": "VEC3",
             "min": lo.tolist(), "max": hi.tolist()},
            {"bufferView": 1, "componentType": 5126, "count": len(P), "type": "VEC3"},
            {"bufferView": 2, "componentType": 5126, "count": len(P), "type": "VEC2"},
            {"bufferView": 3, "componentType": 5126, "count": len(P), "type": "VEC4"},
            {"bufferView": 4, "componentType": 5125, "count": len(I), "type": "SCALAR"},
        ]
        doc = {
            "asset": {"version": "2.0", "generator": "tools/godot/houses/kit.py"},
            "scene": 0, "scenes": [{"nodes": [0]}],
            "nodes": [{"name": "Building", "mesh": 0}],
            "meshes": [{"name": "Building", "primitives": [{
                "attributes": {"POSITION": 0, "NORMAL": 1, "TEXCOORD_0": 2, "COLOR_0": 3},
                "indices": 4, "material": 0}]}],
            "materials": [{"name": material, "pbrMetallicRoughness": {"metallicFactor": 0, "roughnessFactor": 1}}],
            "buffers": [{"byteLength": len(buf)}],
            "bufferViews": views, "accessors": acc,
        }
        js = json.dumps(doc, separators=(",", ":")).encode()
        js += b" " * ((4 - len(js) % 4) % 4)
        out = struct.pack("<III", 0x46546C67, 2, 12 + 8 + len(js) + 8 + len(buf))
        out += struct.pack("<II", len(js), 0x4E4F534A) + js
        out += struct.pack("<II", len(buf), 0x004E4942) + buf
        with open(path, "wb") as f:
            f.write(out)
