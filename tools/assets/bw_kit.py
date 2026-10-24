"""Blender (4.2) kit for the detailed, Foundation-style buildings.

Every part (a plank, a log with its sawn ends, one roof shingle, a footing stone ...) is modelled
as real geometry and UV-mapped into one tile of the shared painted atlas (paint_building_atlas.py),
so a whole building is one mesh with one material. Coordinates are Blender's: metres, Z up, and the
street side of a building (its door) faces +Y, which the FBX export turns into Unity's -Z.

Used by blender_detailed_buildings.py.
"""
import math
import random

import bmesh
import bpy
from mathutils import Euler, Matrix, Vector

TILES = ["planks", "planks_old", "log", "logend",
         "shingle", "shingle_red", "thatch", "thatch_old",
         "stone", "plaster", "cloth_red", "cloth_ochre",
         "hide", "iron", "wattle", "straw"]
N = 4
MARGIN = 6 / 512  # keep UVs this far inside a tile (mip bleeding)

# Metres of the real world one tile covers along (u, v), for parts mapped by size.
TILE_METRES = {
    "planks": (1.0, 1.4), "planks_old": (1.0, 1.4), "log": (0.6, 1.6), "logend": (1, 1),
    "shingle": (1.0, 0.36), "shingle_red": (1.0, 0.36), "thatch": (1.6, 1.0), "thatch_old": (1.6, 1.0),
    "stone": (0.9, 0.9), "plaster": (2.0, 2.0), "cloth_red": (1.4, 1.4), "cloth_ochre": (1.4, 1.4),
    "hide": (1.2, 1.2), "iron": (0.6, 0.6), "wattle": (1.6, 1.0), "straw": (1.0, 1.0),
}


def tile_rect(name):
    i = TILES.index(name)
    row, col = divmod(i, N)
    u0 = col / N
    v0 = 1 - (row + 1) / N
    return u0, v0


class Kit:
    """Collects the parts of one building; build() makes the Blender object."""

    def __init__(self, seed=1):
        self.rng = random.Random(seed)
        self.verts = []
        self.faces = []
        self.uvs = []      # per face: list of (u, v) per corner
        self.smooth = []

    # MARK: - low level

    def _add_bm(self, bm, matrix, tile, grain_axis=2, size=None, smooth=False, uv_fn=None, sub=None):
        """Appends a temporary bmesh (in part-local space) transformed by matrix.
        UVs: per-face box projection in local space, the grain along grain_axis (0 x, 1 y, 2 z)."""
        bm.verts.ensure_lookup_table()
        bm.verts.index_update()
        bm.normal_update()
        base = len(self.verts)
        lo = Vector((min(v.co.x for v in bm.verts), min(v.co.y for v in bm.verts), min(v.co.z for v in bm.verts)))
        hi = Vector((max(v.co.x for v in bm.verts), max(v.co.y for v in bm.verts), max(v.co.z for v in bm.verts)))
        ext = hi - lo
        for v in bm.verts:
            self.verts.append(matrix @ v.co)
        u0, v0 = tile_rect(tile)
        tw, th = TILE_METRES[tile]
        # Random sub-rectangle of the tile, sized by the part.
        if sub is None:
            sub = self._sub(tile, ext, grain_axis)
        flip = matrix.to_3x3().determinant() < 0
        for f in bm.faces:
            idx = [base + v.index for v in f.verts]
            if uv_fn is not None:
                local = uv_fn(f)
            else:
                n = f.normal
                a = max(range(3), key=lambda k: abs(n[k]))
                axes = [k for k in range(3) if k != a]
                if grain_axis in axes:
                    va = grain_axis
                    ua = axes[0] if axes[1] == grain_axis else axes[1]
                else:
                    ua, va = axes
                local = []
                for v in f.verts:
                    uu = (v.co[ua] - lo[ua]) / max(1e-6, ext[ua])
                    vv = (v.co[va] - lo[va]) / max(1e-6, ext[va])
                    local.append((uu, vv))
            su0, sv0, sw, sh = sub
            uv = []
            for (uu, vv) in local:
                uv.append((u0 + (MARGIN + (su0 + uu * sw) * (1 - 2 * MARGIN)) / N,
                           v0 + (MARGIN + (sv0 + vv * sh) * (1 - 2 * MARGIN)) / N))
            if flip:
                idx, uv = idx[::-1], uv[::-1]
            self.faces.append(idx)
            self.uvs.append(uv)
            self.smooth.append(smooth)
        bm.free()

    def _sub(self, tile, ext, grain_axis):
        tw, th = TILE_METRES[tile]
        dims = sorted([ext[k] for k in range(3) if k != grain_axis], reverse=True)
        across = dims[0] if dims else 0.2
        along = ext[grain_axis]
        sw = min(1.0, max(0.05, across / tw))
        sh = min(1.0, max(0.05, along / th))
        return (self.rng.uniform(0, 1 - sw), self.rng.uniform(0, 1 - sh), sw, sh)

    @staticmethod
    def matrix(pos, rot=(0, 0, 0)):
        return Matrix.Translation(Vector(pos)) @ Euler(tuple(math.radians(r) for r in rot), "XYZ").to_matrix().to_4x4()

    # MARK: - parts

    def box(self, pos, size, rot=(0, 0, 0), tile="planks", bevel=0.012, grain=2, jitter=0.0, sub=None, matrix=None):
        """A bevelled box centred on pos (size x, y, z), grain along local axis `grain`."""
        bm = bmesh.new()
        bmesh.ops.create_cube(bm, size=1.0)
        for v in bm.verts:
            v.co = Vector((v.co.x * size[0], v.co.y * size[1], v.co.z * size[2]))
        if bevel > 0:
            b = min(bevel, min(size) * 0.45)
            bmesh.ops.bevel(bm, geom=list(bm.edges), offset=b, segments=1, affect="EDGES", profile=0.5)
        if jitter > 0:
            for v in bm.verts:
                v.co += Vector((self.rng.uniform(-jitter, jitter) for _ in range(3)))
        m = matrix if matrix is not None else self.matrix(pos, rot)
        self._add_bm(bm, m, tile, grain_axis=grain, sub=sub)

    def beam(self, a, b, w=0.12, h=None, tile="planks", bevel=0.015, roll=0.0):
        """A squared timber from a to b."""
        h = h or w
        a, b = Vector(a), Vector(b)
        d = b - a
        L = d.length
        m = Matrix.Translation((a + b) / 2) @ d.to_track_quat("Z", "Y").to_matrix().to_4x4() @ Matrix.Rotation(math.radians(roll), 4, "Z")
        self.box((0, 0, 0), (w, h, L), tile=tile, bevel=bevel, grain=2, matrix=m)

    def log(self, a, b, r=0.08, sides=9, ends=True, tile="log", taper=0.9, wobble=0.08, overhang_ring=True, end_tile="logend", uv_around=False):
        """A debarked log from a to b with sawn end rings (logend tile) and a chamfer at each end."""
        a, b = Vector(a), Vector(b)
        d = b - a
        L = d.length
        rot = d.to_track_quat("Z", "Y").to_matrix().to_4x4()
        spin = self.rng.uniform(0, math.tau)
        m = Matrix.Translation(a) @ rot @ Matrix.Rotation(spin, 4, "Z")
        bm = bmesh.new()
        rings = []
        cham = min(r * 0.25, L * 0.1)
        # Rings: start (chamfer), body ..., end (chamfer). The body bows slightly.
        zs = [0.0, cham] + [L * t for t in (0.33, 0.66)] + [L - cham, L]
        offs = [Vector((self.rng.uniform(-1, 1) * r * wobble, self.rng.uniform(-1, 1) * r * wobble, 0)) for _ in zs]
        offs[0] = offs[1]
        offs[-1] = offs[-2]
        for k, z in enumerate(zs):
            t = z / max(L, 1e-6)
            rr = r * (1 - (1 - taper) * t)
            if k in (0, len(zs) - 1):
                rr *= 0.86
            ring = []
            for s in range(sides):
                ang = s / sides * math.tau
                jitter = 1 + self.rng.uniform(-0.04, 0.04)
                ring.append(bm.verts.new((math.cos(ang) * rr * jitter + offs[k].x, math.sin(ang) * rr * jitter + offs[k].y, z)))
            rings.append(ring)
        side_faces = []
        for k in range(len(rings) - 1):
            for s in range(sides):
                f = bm.faces.new((rings[k][s], rings[k][(s + 1) % sides], rings[k + 1][(s + 1) % sides], rings[k + 1][s]))
                f.smooth = True
                side_faces.append((f, k, s))
        cap_faces = []
        if ends:
            cap_faces.append(bm.faces.new(list(reversed(rings[0]))))
            cap_faces.append(bm.faces.new(rings[-1]))
        bm.verts.index_update()
        bm.faces.index_update()
        # Side UVs (log tile): u around, v along; caps: disc on the logend tile.
        u0, v0 = tile_rect(tile)
        tw, th = TILE_METRES[tile]
        sv = min(1.0, L / th)
        sv0 = self.rng.uniform(0, 1 - sv)
        su = min(1.0, (math.tau * r) / tw)
        base = len(self.verts)
        for v in bm.verts:
            self.verts.append(m @ v.co)
        for f, k, s in side_faces:
            corners = []
            for v in f.verts:
                ss = None
                for kk, ring in enumerate(rings):
                    if v in ring:
                        ss = ring.index(v)
                        z = zs[kk]
                        break
                uu = ss / sides
                if f.verts[0] is rings[k][s] and ss == 0 and s == sides - 1:
                    uu = 1.0
                if ss == 0 and s == sides - 1:
                    uu = 1.0
                corners.append((uu, z / max(L, 1e-6)))
            if uv_around:
                uv = [(u0 + (MARGIN + (sv0 * 0 + vv * min(1.0, L / tw)) * (1 - 2 * MARGIN)) / N, v0 + (MARGIN + (uu * min(1.0, math.tau * r / th)) * (1 - 2 * MARGIN)) / N) for uu, vv in corners]
            else:
                uv = [(u0 + (MARGIN + (uu * su) * (1 - 2 * MARGIN)) / N, v0 + (MARGIN + (sv0 + vv * sv) * (1 - 2 * MARGIN)) / N) for uu, vv in corners]
            self.faces.append([base + v.index for v in f.verts])
            self.uvs.append(uv)
            self.smooth.append(True)
        e0, ev0 = tile_rect(end_tile)
        for f in cap_faces:
            uv = []
            ring = rings[0] if f.verts[0] in rings[0] else rings[-1]
            for v in f.verts:
                s = ring.index(v)
                ang = s / sides * math.tau
                rad = 0.47 if end_tile == "logend" else 0.15
                uv.append((e0 + (0.5 + math.cos(ang) * rad) / N, ev0 + (0.5 + math.sin(ang) * rad) / N))
            self.faces.append([base + v.index for v in f.verts])
            self.uvs.append(uv)
            self.smooth.append(False)
        bm.free()

    def rock(self, pos, size, rot=None, tile="stone", subdiv=1, rough=0.18, flat_bottom=True, faceted=False):
        """A rounded painted stone (icosphere squashed to size, jittered)."""
        bm = bmesh.new()
        bmesh.ops.create_icosphere(bm, subdivisions=subdiv, radius=0.5)
        for v in bm.verts:
            k = 1 + self.rng.uniform(-rough, rough)
            v.co = Vector((v.co.x * size[0] * k, v.co.y * size[1] * k, v.co.z * size[2] * k))
            if flat_bottom and v.co.z < -size[2] * 0.25:
                v.co.z = -size[2] * 0.25 + (v.co.z + size[2] * 0.25) * 0.3
        rot = rot if rot is not None else (self.rng.uniform(-6, 6), self.rng.uniform(-6, 6), self.rng.uniform(0, 360))
        m = self.matrix(pos, rot)

        def uv_fn(f, lo=None):
            out = []
            for v in f.verts:
                n = v.co.normalized() if v.co.length > 1e-6 else Vector((0, 0, 1))
                out.append((0.5 + math.atan2(n.y, n.x) / math.tau * 0.9, 0.5 + n.z * 0.45))
            # Avoid the seam wrapping all the way across.
            us = [p[0] for p in out]
            if max(us) - min(us) > 0.5:
                out = [((u + 0.45) % 0.9 if u < 0.5 else u, v) for u, v in out]
            return out
        self._add_bm(bm, m, tile, smooth=not faceted, uv_fn=uv_fn, sub=(self.rng.uniform(0, 0.4), self.rng.uniform(0, 0.4), 0.6, 0.6))

    def quad_strip(self, pts_bottom, pts_top, tile, smooth=False, sub=None):
        """A sheet between two polylines (cloth, hide), two-sided (front and back faces on their own verts)."""
        n = len(pts_bottom)
        u0, v0 = tile_rect(tile)
        sub = sub or (0, 0, 1, 1)

        def uvp(j, top):
            uu = j / max(1, n - 1)
            vv = 1.0 if top else 0.0
            return (u0 + (MARGIN + (sub[0] + uu * sub[2]) * (1 - 2 * MARGIN)) / N,
                    v0 + (MARGIN + (sub[1] + vv * sub[3]) * (1 - 2 * MARGIN)) / N)
        b0, b1, t0 = Vector(pts_bottom[0]), Vector(pts_bottom[-1]), Vector(pts_top[0])
        nrm = (b1 - b0).cross(t0 - b0)
        nrm = nrm.normalized() if nrm.length > 1e-9 else Vector((0, 0, 1))
        for back in (False, True):
            base = len(self.verts)
            push = -nrm * 0.004 if back else Vector((0, 0, 0))
            for p in pts_bottom:
                self.verts.append(Vector(p) + push)
            for p in pts_top:
                self.verts.append(Vector(p) + push)
            for i in range(n - 1):
                idx = [base + i, base + i + 1, base + n + i + 1, base + n + i]
                uv = [uvp(i, False), uvp(i + 1, False), uvp(i + 1, True), uvp(i, True)]
                if back:
                    idx, uv = idx[::-1], uv[::-1]
                self.faces.append(idx)
                self.uvs.append(uv)
                self.smooth.append(smooth)

    # MARK: - assemblies

    def shingle_roof(self, origin, along, up, length, slope_len, tile="shingle", row=0.15, width=(0.17, 0.25),
                     thick=0.032, deck=True, droop=0.0):
        """Overlapping rows of wooden shingles on the plane origin + along*s + up*t (along, up unit).
        Each shingle is a bevelled board, tilted so its lower edge lifts, with jittered size and yaw."""
        O, A, U = Vector(origin), Vector(along).normalized(), Vector(up).normalized()
        Nrm = A.cross(U).normalized()
        if Nrm.z < 0:
            Nrm = -Nrm
        shingle_len = row * 2.3
        rows = max(1, int(math.ceil(slope_len / row)))
        if deck:
            c = O + A * (length / 2) + U * (slope_len / 2) - Nrm * 0.03
            m = Matrix((A, U, Nrm)).transposed().to_4x4()
            m.translation = c
            self.box((0, 0, 0), (length, slope_len, 0.04), tile="planks_old", bevel=0.0, grain=1, matrix=m)
        for r in range(rows):
            t = r * row
            s = -0.06 + (self.rng.uniform(0, width[1]) if r % 2 else 0)
            while s < length + 0.04:
                w = self.rng.uniform(*width)
                if s + w > length + 0.06:
                    w = max(0.08, length + 0.06 - s)
                L = min(shingle_len, slope_len - t + 0.06)
                centre = O + A * (s + w / 2) + U * (t + L / 2) + Nrm * (thick * 0.6 + r * 0.0015)
                centre -= Nrm * droop * math.sin(min(1, (s + w / 2) / max(length, 0.01)) * math.pi)
                tilt = math.radians(self.rng.uniform(4, 9))
                yaw = math.radians(self.rng.uniform(-3, 3))
                basis = Matrix((A, U, Nrm)).transposed()
                local = Matrix.Rotation(yaw, 3, "Z") @ Matrix.Rotation(-tilt, 3, "X")
                m = (basis @ local).to_4x4()
                m.translation = centre + Nrm * math.sin(tilt) * L * 0.4
                col = self.rng.randrange(4)
                sub = (col * 0.25 + 0.01, self.rng.uniform(0, 0.1), 0.23, 0.9)
                self.box((0, 0, 0), (w * 0.96, L, thick * self.rng.uniform(0.85, 1.2)), tile=tile if self.rng.random() > 0.06 else ("shingle_red" if tile == "shingle" else "shingle"),
                         bevel=0.0, grain=1, sub=sub, matrix=m)
                s += w
        return Nrm

    def thatch_roof(self, origin, along, up, length, slope_len, thick=0.22, courses=5, tile="thatch", overhang=0.0):
        """A thick, shaggy thatch slope: a base slab, a rounded roll at the eave, courses of thatch that
        bulge and overlap with ragged lower edges, and straw tufts hanging over every course edge."""
        O, A, U = Vector(origin), Vector(along).normalized(), Vector(up).normalized()
        Nrm = A.cross(U).normalized()
        if Nrm.z < 0:
            Nrm = -Nrm
        basis = Matrix((A, U, Nrm)).transposed().to_4x4()
        m = basis.copy()
        m.translation = O + A * (length / 2) + U * (slope_len / 2) + Nrm * (thick * 0.3)
        self.box((0, 0, 0), (length, slope_len, thick * 0.6), tile="thatch_old", bevel=0.05, grain=1, matrix=m)
        # Rounded eave roll.
        e0 = O + U * 0.02 + Nrm * (thick * 0.42)
        self.log(e0 - A * 0.03, e0 + A * (length + 0.03), r=thick * 0.48, sides=9, tile="thatch", taper=1.0, wobble=0.25, ends=True,
                 end_tile="thatch_old", uv_around=True)
        step = slope_len / courses
        alt = "thatch_old" if tile == "thatch" else "thatch"
        for c in range(courses):
            t0 = c * step + 0.06
            seg = max(5, int(length / 0.14))
            bm = bmesh.new()
            rows = []
            prof = [(-0.02, 0.18), (0.0, 0.62), (step * 0.35, 0.86), (step * 1.4, 0.72)]  # (along slope, height in thick)
            for k, (dt, hz) in enumerate(prof):
                row = []
                for i in range(seg + 1):
                    s = i / seg * length
                    rag = self.rng.uniform(-0.05, 0.03) if k < 2 else 0.0
                    lift = self.rng.uniform(-0.025, 0.025) if k in (1, 2) else 0.0
                    row.append(bm.verts.new((s, min(slope_len, t0 + dt + rag), thick * hz + lift)))
                rows.append(row)
            for k in range(len(rows) - 1):
                for i in range(seg):
                    f = bm.faces.new((rows[k][i], rows[k][i + 1], rows[k + 1][i + 1], rows[k + 1][i]))
                    f.smooth = True
            mm = basis.copy()
            mm.translation = O
            self._add_bm(bm, mm, tile if c % 2 == 0 else alt, grain_axis=1, smooth=True,
                         sub=(self.rng.uniform(0, 0.3), self.rng.uniform(0, 0.2), 0.7, 0.8))
            # Straw tufts over the edge of this course.
            tz = 0
            s = self.rng.uniform(0, 0.05)
            while s < length:
                w = self.rng.uniform(0.05, 0.09)
                hang = self.rng.uniform(0.07, 0.14) * (1.25 if c == 0 else 1.0)
                y0 = t0 + 0.02
                z0 = thick * 0.6
                p0 = O + A * s + U * y0 + Nrm * z0
                p1 = O + A * (s + w) + U * y0 + Nrm * z0
                droop = Nrm * -self.rng.uniform(0.02, 0.06)
                q0 = p0 - U * hang + droop + A * self.rng.uniform(-0.02, 0.02)
                q1 = p1 - U * hang * self.rng.uniform(0.6, 1.0) + droop
                self.quad_strip([q0, q1], [p0, p1], "straw", smooth=True,
                                sub=(self.rng.uniform(0, 0.8), self.rng.uniform(0, 0.6), 0.15, 0.3))
                s += w * self.rng.uniform(0.7, 1.1)
        return Nrm

    def gable_roof(self, cx, cy, z0, width, depth, rise, kind="shingle", overhang=0.22, ridge_log=True, cross_ends=True,
                   thatch_thick=0.24, along_x=True, tile=None):
        """A two-slope roof over a width x depth rectangle centred on (cx, cy), wall tops at z0, ridge `rise`
        above them. The ridge runs along x when along_x (eaves front and back), else along y (gable to the street)."""
        R = Vector((1, 0, 0)) if along_x else Vector((0, 1, 0))
        C = Vector((0, 1, 0)) if along_x else Vector((1, 0, 0))
        span_half = (depth if along_x else width) / 2
        length = (width if along_x else depth) + overhang * 2
        drop = rise * overhang / max(0.01, span_half)
        half = span_half + overhang
        centre = Vector((cx, cy, 0))
        apex = z0 + rise
        for sgn in (-1, 1):
            eave_mid = centre + C * sgn * half + Vector((0, 0, z0 - drop))
            ridge_mid = centre + Vector((0, 0, apex))
            U = (ridge_mid - eave_mid)
            sl = U.length
            U.normalize()
            A = R.copy()
            if A.cross(U).z < 0:
                A = -A
            origin = eave_mid - A * (length / 2)
            if kind == "thatch":
                self.thatch_roof(origin, A, U, length, sl + 0.05, thick=thatch_thick, tile=tile or "thatch")
            else:
                self.shingle_roof(origin, A, U, length, sl + 0.02, tile=tile or "shingle")
        top = apex + (0.07 if kind != "thatch" else thatch_thick * 0.6)
        a = centre - R * (length / 2 + 0.06) + Vector((0, 0, top))
        b = centre + R * (length / 2 + 0.06) + Vector((0, 0, top))
        if kind == "thatch":
            self.log(a, b, r=thatch_thick * 0.62, tile="thatch_old", sides=9, taper=1.0, wobble=0.2, end_tile="thatch_old", uv_around=True)
            # pegged hazel spars holding the ridge
            n = max(2, int(length / 0.45))
            for i in range(n):
                p = a + (b - a) * ((i + 0.5) / n)
                for sgn in (-1, 1):
                    q = p + C * sgn * thatch_thick * 0.9 + Vector((0, 0, -thatch_thick * 0.25))
                    self.log(p + Vector((0, 0, thatch_thick * 0.55)), q, r=0.012, sides=4, tile="log")
        elif ridge_log:
            self.log(a, b, r=0.075, sides=8)
        if cross_ends:
            # Barge logs along both gable edges crossing above the ridge (Foundation-style X ends).
            for e in (-1, 1):
                g = centre + R * e * (length / 2 + 0.03)
                for sgn in (-1, 1):
                    lo = g + C * sgn * (half + 0.03) + Vector((0, 0, z0 - drop - 0.05))
                    hi = g - C * sgn * 0.24 + Vector((0, 0, top + 0.24))
                    self.log(lo, hi, r=0.055, sides=7)

    def plank_wall(self, a, b, z0, height, thick=0.05, board=(0.16, 0.24), tile="planks", gable_rise=0.0, skip=None):
        """Vertical boards from a to b (points on the ground plane), with optional gable triangle on top."""
        a, b = Vector((a[0], a[1], 0)), Vector((b[0], b[1], 0))
        d = b - a
        L = d.length
        dirn = d.normalized()
        normal = Vector((-dirn.y, dirn.x, 0))
        s = 0.0
        while s < L - 0.02:
            w = min(self.rng.uniform(*board), L - s)
            mid = s + w / 2
            if skip and any(lo <= mid <= hi for lo, hi in skip):
                s += w
                continue
            h = height
            if gable_rise > 0:
                h += gable_rise * (1 - abs(mid / L - 0.5) * 2) - gable_rise * (w / (L / 2)) * 0.5 - 0.05
                if h < 0.04:
                    s += w
                    continue
            h += self.rng.uniform(-0.02, 0.03)
            c = a + dirn * mid + Vector((0, 0, z0 + h / 2)) + normal * self.rng.uniform(-0.008, 0.008)
            m = Matrix((dirn, normal, Vector((0, 0, 1)))).transposed().to_4x4()
            m = m @ Matrix.Rotation(math.radians(self.rng.uniform(-1.2, 1.2)), 4, "Y")
            m.translation = c
            self.box((0, 0, 0), (w * 0.97, thick, h), tile=tile, bevel=0.01, grain=2, matrix=m)
            s += w

    def log_wall(self, a, b, z0, height, r=0.09, extend=0.14, skip=None):
        """Horizontal logs stacked from z0 (log cabin), ends sticking out past the corners."""
        a, b = Vector((a[0], a[1], 0)), Vector((b[0], b[1], 0))
        d = (b - a).normalized()
        n = int(height / (r * 1.8))
        for i in range(n):
            z = z0 + r + i * r * 1.8
            if skip:
                # skip: list of (s0, s1, z_top) door/window gaps — split the log around them.
                pieces = [(0.0, (b - a).length)]
                for s0, s1, zt in skip:
                    if z - r < zt:
                        new = []
                        for p0, p1 in pieces:
                            if s1 <= p0 or s0 >= p1:
                                new.append((p0, p1))
                            else:
                                if s0 > p0:
                                    new.append((p0, s0))
                                if s1 < p1:
                                    new.append((s1, p1))
                        pieces = new
                for p0, p1 in pieces:
                    e0 = extend if p0 == 0 else 0
                    e1 = extend if abs(p1 - (b - a).length) < 1e-4 else 0
                    pa = a + d * (p0 - e0) + Vector((0, 0, z))
                    pb = a + d * (p1 + e1) + Vector((0, 0, z))
                    if (pb - pa).length > 0.05:
                        self.log(pa, pb, r=r * self.rng.uniform(0.92, 1.05), sides=8)
            else:
                pa = a - d * extend + Vector((0, 0, z))
                pb = b + d * extend + Vector((0, 0, z))
                self.log(pa, pb, r=r * self.rng.uniform(0.92, 1.05), sides=8)

    def stone_footing(self, x0, y0, x1, y1, h=0.22, size=0.24, gaps=None):
        """A dry-stone footing along the rectangle's edges."""
        edges = [((x0, y0), (x1, y0)), ((x1, y0), (x1, y1)), ((x1, y1), (x0, y1)), ((x0, y1), (x0, y0))]
        for (ax, ay), (bx, by) in edges:
            L = math.hypot(bx - ax, by - ay)
            n = max(2, int(L / size))
            for i in range(n):
                t = (i + 0.5) / n
                x, y = ax + (bx - ax) * t, ay + (by - ay) * t
                if gaps and any(math.hypot(x - gx, y - gy) < gr for gx, gy, gr in gaps):
                    continue
                s = L / n * self.rng.uniform(1.1, 1.3)
                yaw = math.degrees(math.atan2(by - ay, bx - ax)) + self.rng.uniform(-8, 8)
                self.rock((x, y, h * 0.3), (s, s * 0.75, h * self.rng.uniform(0.65, 0.8)), rot=(self.rng.uniform(-4, 4), self.rng.uniform(-4, 4), yaw),
                          subdiv=2, rough=0.08)
                if i < n - 1:
                    t2 = (i + 1) / n
                    x2, y2 = ax + (bx - ax) * t2, ay + (by - ay) * t2
                    if not (gaps and any(math.hypot(x2 - gx, y2 - gy) < gr for gx, gy, gr in gaps)):
                        self.rock((x2, y2, h * 0.72), (s * 0.8, s * 0.6, h * 0.5), rot=(0, 0, yaw + self.rng.uniform(-10, 10)), subdiv=1, rough=0.1)

    def door(self, cx, cy, z0, w=0.55, h=1.0, facing=(0, 1), open_angle=0.0):
        """Plank door with two ledges and iron straps in a frame of posts and a lintel."""
        fx, fy = facing
        side = Vector((fy, -fx, 0)) if (fx, fy) != (0, 1) else Vector((1, 0, 0))
        nrm = Vector((fx, fy, 0))
        c = Vector((cx, cy, z0))
        # frame
        self.beam(c - side * (w / 2 + 0.05), c - side * (w / 2 + 0.05) + Vector((0, 0, h + 0.08)), w=0.09, tile="planks")
        self.beam(c + side * (w / 2 + 0.05), c + side * (w / 2 + 0.05) + Vector((0, 0, h + 0.08)), w=0.09, tile="planks")
        self.beam(c - side * (w / 2 + 0.14) + Vector((0, 0, h + 0.1)), c + side * (w / 2 + 0.14) + Vector((0, 0, h + 0.1)), w=0.1, tile="log")
        # dark interior behind
        m = Matrix((side, nrm, Vector((0, 0, 1)))).transposed().to_4x4()
        m.translation = c + Vector((0, 0, h / 2)) - nrm * 0.03
        self.box((0, 0, 0), (w, 0.02, h), tile="iron", bevel=0, matrix=m, sub=(0.1, 0.1, 0.2, 0.2))
        # door leaf, swung open by open_angle about its left edge
        hinge = c - side * (w / 2) + nrm * 0.02
        rot = Matrix.Rotation(math.radians(open_angle), 4, "Z")
        boards = 4
        for i in range(boards):
            bw = w / boards
            local = Vector(((i + 0.5) * bw, 0, h / 2))
            mm = Matrix((side, nrm, Vector((0, 0, 1)))).transposed().to_4x4()
            mm = Matrix.Translation(hinge) @ rot @ Matrix.Translation(-hinge) @ Matrix.Translation(hinge + side * local.x + Vector((0, 0, local.z))) @ Matrix((side, nrm, Vector((0, 0, 1)))).transposed().to_4x4()
            self.box((0, 0, 0), (bw * 0.96, 0.04, h - 0.03 + self.rng.uniform(-0.02, 0.02)), tile="planks", bevel=0.008, grain=2, matrix=mm)
        for zz in (0.22, h - 0.22):
            mm = Matrix.Translation(hinge) @ rot @ Matrix.Translation(-hinge) @ Matrix.Translation(hinge + side * (w / 2) + nrm * 0.03 + Vector((0, 0, zz))) @ Matrix((side, nrm, Vector((0, 0, 1)))).transposed().to_4x4()
            self.box((0, 0, 0), (w * 0.95, 0.03, 0.09), tile="planks_old", bevel=0.008, grain=0, matrix=mm)
            mm2 = Matrix.Translation(hinge) @ rot @ Matrix.Translation(-hinge) @ Matrix.Translation(hinge + side * (w * 0.2) + nrm * 0.05 + Vector((0, 0, zz))) @ Matrix((side, nrm, Vector((0, 0, 1)))).transposed().to_4x4()
            self.box((0, 0, 0), (w * 0.38, 0.012, 0.04), tile="iron", bevel=0.004, grain=0, matrix=mm2)

    def window(self, cx, cy, z, w=0.36, h=0.32, facing=(0, 1), shutters=True):
        """A small window: dark opening, timber frame, two plank shutters swung open."""
        fx, fy = facing
        nrm = Vector((fx, fy, 0))
        side = Vector((fy, -fx, 0)) if (fx, fy) != (0, 1) else Vector((1, 0, 0))
        c = Vector((cx, cy, z))
        basis = Matrix((side, nrm, Vector((0, 0, 1)))).transposed().to_4x4()
        m = basis.copy()
        m.translation = c - nrm * 0.01
        self.box((0, 0, 0), (w, 0.03, h), tile="iron", bevel=0, matrix=m, sub=(0.1, 0.1, 0.2, 0.2))
        for dz in (-h / 2 - 0.03, h / 2 + 0.03):
            mm = basis.copy()
            mm.translation = c + nrm * 0.02 + Vector((0, 0, dz))
            self.box((0, 0, 0), (w + 0.14, 0.07, 0.06), tile="planks", bevel=0.01, grain=0, matrix=mm)
        for dx in (-w / 2 - 0.03, w / 2 + 0.03):
            mm = basis.copy()
            mm.translation = c + nrm * 0.02 + side * dx
            self.box((0, 0, 0), (0.05, 0.06, h + 0.06), tile="planks", bevel=0.01, grain=2, matrix=mm)
        # mullion
        mm = basis.copy()
        mm.translation = c + nrm * 0.01
        self.box((0, 0, 0), (0.03, 0.03, h), tile="planks_old", bevel=0.005, grain=2, matrix=mm)
        if shutters:
            for sgn in (-1, 1):
                hinge = c + side * sgn * (w / 2 + 0.06) + nrm * 0.03
                ang = math.radians(sgn * -60)
                rot = Matrix.Rotation(ang, 4, "Z")
                for i in range(2):
                    off = side * sgn * (w / 4 * (i + 0.5))
                    mm = Matrix.Translation(hinge) @ rot @ Matrix.Translation(off) @ Matrix((side, nrm, Vector((0, 0, 1)))).transposed().to_4x4()
                    self.box((0, 0, 0), (w / 4 * 0.95, 0.03, h + 0.04), tile="planks_old", bevel=0.006, grain=2, matrix=mm)

    # MARK: - props

    def barrel(self, pos, h=0.42, r=0.15, lid=True):
        """Staves with a bulge, two iron hoops and a lid."""
        x, y, z = pos
        sides = 10
        bm = bmesh.new()
        prof = [(0, 0.86), (0.25, 0.97), (0.5, 1.0), (0.75, 0.97), (1.0, 0.86)]
        rings = []
        for t, k in prof:
            rings.append([bm.verts.new((math.cos(s / sides * math.tau) * r * k, math.sin(s / sides * math.tau) * r * k, t * h)) for s in range(sides)])
        for i in range(len(rings) - 1):
            for s in range(sides):
                f = bm.faces.new((rings[i][s], rings[i][(s + 1) % sides], rings[i + 1][(s + 1) % sides], rings[i + 1][s]))
                f.smooth = True
        top = bm.faces.new(rings[-1])
        bm.faces.new(list(reversed(rings[0])))
        m = self.matrix((x, y, z), (0, 0, self.rng.uniform(0, 360)))
        self._add_bm(bm, m, "planks", grain_axis=2, smooth=True, sub=(0, self.rng.uniform(0, 0.5), 1.0, 0.4))
        for t in (0.18, 0.82):
            k = 0.97 + 0.01
            self.log((x, y, z + t * h - 0.02), (x, y, z + t * h + 0.025), r=r * k + 0.008, sides=sides, tile="iron", ends=False, taper=1, wobble=0)
        if lid:
            self.log((x, y, z + h - 0.03), (x, y, z + h + 0.005), r=r * 0.84, sides=sides, tile="planks", taper=1, wobble=0)

    def crate(self, pos, s=0.36, yaw=0.0, tile="planks"):
        x, y, z = pos
        m = self.matrix((x, y, z + s / 2), (0, 0, yaw))
        self.box((0, 0, 0), (s * 0.94, s * 0.94, s * 0.94), tile=tile, bevel=0.01, matrix=m)
        # frame battens on the front/back and sides
        for dx in (-1, 1):
            for dy in (-1, 1):
                mm = m @ Matrix.Translation((dx * s * 0.47, dy * s * 0.47, 0))
                self.box((0, 0, 0), (0.05, 0.05, s), tile="planks_old", bevel=0.008, matrix=mm)
        for dz in (-1, 1):
            for dy in (-1, 1):
                mm = m @ Matrix.Translation((0, dy * s * 0.48, dz * s * 0.45))
                self.box((0, 0, 0), (s, 0.04, 0.06), tile="planks_old", bevel=0.008, grain=0, matrix=mm)

    def sack(self, pos, s=0.3, tile="hide"):
        x, y, z = pos
        self.rock((x, y, z + s * 0.35), (s * 0.85, s * 0.75, s * 0.9), tile=tile, subdiv=2, rough=0.06)
        self.rock((x, y, z + s * 0.8), (s * 0.3, s * 0.3, s * 0.18), tile=tile, subdiv=1, rough=0.05)

    def log_pile(self, x, y, z, length=1.4, rows=3, r=0.1, along_x=True):
        """Logs stacked in a pyramid with chocks."""
        for row in range(rows):
            n = rows - row + 1
            for i in range(n):
                off = (i - (n - 1) / 2) * r * 2.05
                zz = z + r + row * r * 1.75
                ll = length * self.rng.uniform(0.9, 1.05)
                shift = self.rng.uniform(-0.06, 0.06)
                if along_x:
                    self.log((x - ll / 2 + shift, y + off, zz), (x + ll / 2 + shift, y + off, zz), r=r * self.rng.uniform(0.85, 1.1))
                else:
                    self.log((x + off, y - ll / 2 + shift, zz), (x + off, y + ll / 2 + shift, zz), r=r * self.rng.uniform(0.85, 1.1))
        # stakes holding the pile
        w = (rows + 1) * r * 1.05 + 0.06
        for e in (-1, 1):
            for k in (-0.3, 0.3):
                if along_x:
                    self.log((x + k * length, y + e * w, z), (x + k * length, y + e * w * 0.95, z + rows * r * 1.9), r=0.035, sides=6)
                else:
                    self.log((x + e * w, y + k * length, z), (x + e * w * 0.95, y + k * length, z + rows * r * 1.9), r=0.035, sides=6)

    def firewood(self, x, y, z, w=0.7, h=0.5, depth=0.3):
        """A neat stack of split firewood: rows of short log ends facing out."""
        r = 0.045
        rows = int(h / (r * 1.9))
        cols = int(w / (r * 2.1))
        for i in range(rows):
            for j in range(cols):
                px = x - w / 2 + (j + 0.5 + (i % 2) * 0.5) * (w / cols)
                if px > x + w / 2:
                    continue
                pz = z + r + i * r * 1.85
                self.log((px, y - depth / 2, pz), (px + self.rng.uniform(-0.02, 0.02), y + depth / 2, pz), r=r * self.rng.uniform(0.8, 1.15), sides=6)

    def ladder(self, a, b, w=0.34, rungs=None):
        a, b = Vector(a), Vector(b)
        d = (b - a)
        side = d.cross(Vector((0, 0, 1)))
        if side.length < 1e-4:
            side = Vector((1, 0, 0))
        side = side.normalized() * (w / 2)
        self.log(a - side, b - side, r=0.03, sides=6)
        self.log(a + side, b + side, r=0.03, sides=6)
        n = rungs or max(2, int(d.length / 0.28))
        for i in range(1, n + 1):
            p = a + d * (i / (n + 1))
            self.log(p - side * 1.15, p + side * 1.15, r=0.02, sides=5)

    def sawhorse(self, x, y, z=0.0, yaw=0.0, L=0.8, h=0.5):
        m = self.matrix((x, y, z), (0, 0, yaw))
        def P(px, py, pz):
            return m @ Vector((px, py, pz))
        self.log(P(-L / 2, 0, h), P(L / 2, 0, h), r=0.06, sides=7)
        for e in (-1, 1):
            self.beam(P(e * L * 0.4, -0.22, 0), P(e * L * 0.4, 0.04, h + 0.06), w=0.05, tile="planks")
            self.beam(P(e * L * 0.4, 0.22, 0), P(e * L * 0.4, -0.04, h + 0.06), w=0.05, tile="planks")

    def bench(self, x, y, z=0.0, yaw=0.0, L=0.9):
        m = self.matrix((x, y, z), (0, 0, yaw))
        top = m @ Matrix.Translation((0, 0, 0.32))
        self.box((0, 0, 0), (L, 0.24, 0.05), tile="planks", bevel=0.01, grain=0, matrix=top)
        for e in (-1, 1):
            mm = m @ Matrix.Translation((e * L * 0.38, 0, 0.15))
            self.box((0, 0, 0), (0.05, 0.2, 0.3), tile="planks", bevel=0.008, matrix=mm)

    def awning(self, a, b, depth=0.55, drop=0.25, tile="cloth_red", poles=True, sag=0.05):
        """A cloth awning hung from the line a-b out to the front (+normal), on two poles."""
        a, b = Vector(a), Vector(b)
        d = (b - a)
        n = Vector((-d.y, d.x, 0)).normalized()
        seg = 6
        top, bot = [], []
        for i in range(seg + 1):
            t = i / seg
            p = a + d * t
            s = math.sin(t * math.pi) * sag
            top.append(p.copy())
            bot.append(p + n * depth - Vector((0, 0, drop + s)))
        self.quad_strip(bot, top, tile)
        if poles:
            for p in (bot[0], bot[-1]):
                self.log((p.x, p.y, 0), (p.x, p.y, p.z + 0.06), r=0.035, sides=6)
            self.log(bot[0] + Vector((0, 0, 0.02)), bot[-1] + Vector((0, 0, 0.02)), r=0.03, sides=6)

    def hay(self, x, y, z=0.0, s=0.5):
        m = self.matrix((x, y, z + s * 0.32), (0, 0, self.rng.uniform(0, 90)))
        self.box((0, 0, 0), (s, s * 0.6, s * 0.62), tile="straw", bevel=0.06, matrix=m, jitter=0.012)

    def stump(self, x, y, z=0.0, r=0.16, h=0.32, axe=True):
        self.log((x, y, z - 0.02), (x, y, z + h), r=r, sides=10, taper=0.95)
        if axe:
            self.log((x + 0.02, y, z + h), (x + 0.28, y + 0.1, z + h + 0.32), r=0.018, sides=5, tile="planks")
            self.box((x + 0.03, y - 0.0, z + h + 0.04), (0.12, 0.025, 0.08), (0, 0, 20), tile="iron", bevel=0.005)

    def pole_frame(self, x, y, w, h, yaw=0.0, tile_hang=None, n_hang=4):
        """Two posts and a cross pole with things (hides, fish, herbs) hanging from it."""
        m = self.matrix((x, y, 0), (0, 0, yaw))
        P = lambda px, py, pz: m @ Vector((px, py, pz))
        self.log(P(-w / 2, 0, 0), P(-w / 2, 0, h), r=0.04, sides=6)
        self.log(P(w / 2, 0, 0), P(w / 2, 0, h), r=0.04, sides=6)
        self.log(P(-w / 2 - 0.1, 0, h - 0.04), P(w / 2 + 0.1, 0, h - 0.04), r=0.035, sides=6)
        if tile_hang:
            for i in range(n_hang):
                px = -w / 2 + (i + 0.5) * w / n_hang
                hw = w / n_hang * 0.75
                hh = self.rng.uniform(0.3, 0.5)
                top = [P(px - hw / 2, 0, h - 0.06), P(px + hw / 2, 0, h - 0.06)]
                bot = [P(px - hw / 2 * 0.8, 0.02, h - 0.06 - hh), P(px + hw / 2 * 0.8, 0.02, h - 0.06 - hh)]
                self.quad_strip(bot, top, tile_hang, sub=(self.rng.uniform(0, 0.5), self.rng.uniform(0, 0.5), 0.45, 0.45))

    def ground_patch(self, x0, y0, x1, y1, tile="planks_old"):
        """A plank floor or platform."""
        L = x1 - x0
        n = max(1, int((y1 - y0) / 0.2))
        for i in range(n):
            yy = y0 + (i + 0.5) * (y1 - y0) / n
            self.box(((x0 + x1) / 2, yy, 0.03), (L * self.rng.uniform(0.97, 1.0), (y1 - y0) / n * 0.95, 0.05), tile=tile, bevel=0.008, grain=0)

    def lathe(self, pos, profile, tile, sides=10, cap_top=True, sub=None):
        """A turned shape (pot, basket, stone dome): profile = [(radius, z), ...] bottom to top."""
        x, y, z = pos
        bm = bmesh.new()
        rings = []
        for r, zz in profile:
            rings.append([bm.verts.new((math.cos(s / sides * math.tau) * r, math.sin(s / sides * math.tau) * r, zz)) for s in range(sides)])
        for i in range(len(rings) - 1):
            for s_ in range(sides):
                f = bm.faces.new((rings[i][s_], rings[i][(s_ + 1) % sides], rings[i + 1][(s_ + 1) % sides], rings[i + 1][s_]))
                f.smooth = True
        bm.faces.new(list(reversed(rings[0])))
        if cap_top:
            bm.faces.new(rings[-1])
        m = self.matrix((x, y, z), (0, 0, self.rng.uniform(0, 360)))
        self._add_bm(bm, m, tile, grain_axis=2, smooth=True, sub=sub)

    def pot(self, pos, h=0.3, r=0.13, tile="hide"):
        self.lathe(pos, [(r * 0.55, 0), (r * 0.95, h * 0.25), (r, h * 0.5), (r * 0.7, h * 0.85), (r * 0.5, h * 0.92), (r * 0.62, h)], tile, cap_top=True,
                   sub=(self.rng.uniform(0, 0.5), self.rng.uniform(0, 0.5), 0.4, 0.4))

    def basket(self, pos, h=0.22, r=0.17, full="straw"):
        x, y, z = pos
        self.lathe(pos, [(r * 0.8, 0), (r, h * 0.7), (r * 1.05, h)], "wattle", cap_top=False, sub=(0, 0, 0.5, 0.3))
        self.lathe((x, y, z + h * 0.75), [(r * 0.98, 0), (r * 0.7, h * 0.3), (0.01, h * 0.38)], full, cap_top=False,
                   sub=(self.rng.uniform(0, 0.5), self.rng.uniform(0, 0.5), 0.4, 0.4))

    def cone_thatch(self, cx, cy, z0, radius, height, courses=5, tile="thatch", sides=18):
        """A conical thatch roof: overlapping rings of thatch with ragged lower edges, tufts, a knot on top."""
        alt = "thatch_old" if tile == "thatch" else "thatch"
        c = Vector((cx, cy, z0))
        apex = c + Vector((0, 0, height))
        slant = math.hypot(radius, height)
        for k in range(courses):
            t0 = k / courses
            t1 = min(1.0, (k + 1.4) / courses)
            bm = bmesh.new()
            rows = []
            for j, (t, out) in enumerate(((t0 - 0.01, 0.04), (t0 + 0.03, 0.12), (t1, 0.08))):
                row = []
                for i in range(sides):
                    ang = i / sides * math.tau
                    rag = self.rng.uniform(-0.03, 0.03) if j < 2 else 0
                    rr = radius * (1 - t - rag) + out * (1 - t) + (0.02 if j == 1 else 0)
                    zz = z0 + height * (t + rag) + (0.02 if j == 1 else 0)
                    row.append(bm.verts.new((cx + math.cos(ang) * rr, cy + math.sin(ang) * rr, zz)))
                rows.append(row)
            for j in range(2):
                for i in range(sides):
                    f = bm.faces.new((rows[j][i], rows[j][(i + 1) % sides], rows[j + 1][(i + 1) % sides], rows[j + 1][i]))
                    f.smooth = True

            def uv_fn(f, rows=rows):
                out = []
                for v in f.verts:
                    for j, row in enumerate(rows):
                        if v in row:
                            i = row.index(v)
                            break
                    out.append(((i % (sides // 3)) / (sides // 3), j / 2.0))
                # seam fix
                us = [u for u, _ in out]
                if max(us) - min(us) > 0.5:
                    out = [(u if u > 0.01 else 1.0, v) for u, v in out]
                return out
            self._add_bm(bm, Matrix.Identity(4), tile if k % 2 == 0 else alt, smooth=True, uv_fn=uv_fn,
                         sub=(self.rng.uniform(0, 0.2), 0.0, 0.8, 0.6))
            # tufts
            n = int(math.tau * radius * (1 - t0) / 0.07)
            for i in range(n):
                ang = (i + self.rng.uniform(0, 0.6)) / n * math.tau
                d = Vector((math.cos(ang), math.sin(ang), 0))
                tang = Vector((-d.y, d.x, 0))
                rr = radius * (1 - t0) + 0.06 * (1 - t0)
                p = c + d * rr + Vector((0, 0, height * t0 + 0.03))
                hang = self.rng.uniform(0.08, 0.16) * (1.3 if k == 0 else 1)
                down = (d * radius - Vector((0, 0, -height))).normalized()
                down = Vector((d.x * radius, d.y * radius, -height)).normalized()
                w = self.rng.uniform(0.05, 0.08)
                q = p + down * hang
                self.quad_strip([q - tang * w * 0.4, q + tang * w * 0.4], [p - tang * w / 2, p + tang * w / 2], "straw",
                                smooth=True, sub=(self.rng.uniform(0, 0.8), self.rng.uniform(0, 0.6), 0.15, 0.3))
        # top knot and a pole sticking out
        self.lathe((cx, cy, z0 + height * 0.86), [(radius * 0.2, 0), (radius * 0.16, height * 0.12), (radius * 0.05, height * 0.2)], alt,
                   cap_top=True, sub=(0, 0, 0.5, 0.5))
        self.log((cx, cy, z0 + height * 0.8), (cx + 0.04, cy + 0.03, z0 + height * 1.22), r=0.04, sides=6)

    def lean_to(self, x0, x1, y_low, y_high, z_low, z_high, tile="shingle", overhang=0.2):
        """A single-slope shingle roof from the low eave (y_low, z_low) up to (y_high, z_high)."""
        dy = y_high - y_low
        dz = z_high - z_low
        U = Vector((0, dy, dz)).normalized()
        A = Vector((1, 0, 0))
        if A.cross(U).z < 0:
            A = -A
        length = (x1 - x0) + overhang * 2
        start = Vector(((x0 + x1) / 2, y_low, z_low)) - U * overhang
        origin = start - A * (length / 2)
        self.shingle_roof(origin, A, U, length, math.hypot(dy, dz) + overhang * 1.6, tile=tile)
        top = Vector(((x0 + x1) / 2, y_high, z_high)) + U * overhang * 0.6
        self.log(top - A * (length / 2 + 0.05) + Vector((0, 0, 0.05)), top + A * (length / 2 + 0.05) + Vector((0, 0, 0.05)), r=0.06)

    def wheel(self, pos, r=0.22, w=0.06, axis="x"):
        x, y, z = pos
        a = Vector((x - w / 2, y, z)) if axis == "x" else Vector((x, y - w / 2, z))
        b = Vector((x + w / 2, y, z)) if axis == "x" else Vector((x, y + w / 2, z))
        self.log(a, b, r=r, sides=12, taper=1, wobble=0, tile="planks", end_tile="planks")
        c = (a + b) / 2
        for k in range(3):
            ang = k / 3 * math.pi
            d = Vector((0, math.cos(ang), math.sin(ang))) if axis == "x" else Vector((math.cos(ang), 0, math.sin(ang)))
            o = Vector((w * 0.6, 0, 0)) if axis == "x" else Vector((0, w * 0.6, 0))
            self.beam(c + o - d * r * 0.9, c + o + d * r * 0.9, w=0.03, tile="planks_old")
        self.log(a - (b - a) * 0.6, b + (b - a) * 0.6, r=0.035, sides=6, tile="iron", end_tile="iron")

    def cart(self, x, y, yaw=0.0, load="stone"):
        """A two-wheeled hand cart with shafts, loaded with stones, logs or sacks."""
        m = self.matrix((x, y, 0), (0, 0, yaw))
        def P(px, py, pz):
            return tuple(m @ Vector((px, py, pz)))
        self.box(P(0, 0, 0.36), (0.5, 0.7, 0.05), (0, 0, yaw), tile="planks", grain=1)
        for sx in (-1, 1):
            self.box(P(sx * 0.25, 0, 0.47), (0.04, 0.72, 0.18), (0, 0, yaw), tile="planks_old", grain=1)
        for sy in (-1, 1):
            self.box(P(0, sy * 0.35, 0.47), (0.5, 0.04, 0.18), (0, 0, yaw), tile="planks_old", grain=0)
        for sx in (-1, 1):
            self.beam(P(sx * 0.2, -0.3, 0.33), P(sx * 0.24, 0.75, 0.12), w=0.045, tile="log")
        c = Vector(P(0, -0.05, 0.22))
        ax = (Vector(P(1, 0, 0)) - Vector(P(0, 0, 0))).normalized()
        for sx in (-1, 1):
            p = c + ax * sx * 0.33
            self.log(p - ax * 0.03, p + ax * 0.03, r=0.22, sides=12, taper=1, wobble=0, tile="planks", end_tile="planks")
        self.log(c - ax * 0.4, c + ax * 0.4, r=0.03, sides=6, tile="iron", end_tile="iron")
        if load == "stone":
            for i in range(5):
                self.rock(P(self.rng.uniform(-0.15, 0.15), self.rng.uniform(-0.22, 0.22), 0.48 + (i // 3) * 0.08), (0.2, 0.17, 0.15), rough=0.15)
        elif load == "logs":
            for i in range(3):
                self.log(P(-0.15 + i * 0.15, -0.4, 0.5), P(-0.15 + i * 0.15, 0.45, 0.5 + (i % 2) * 0.03), r=0.07)

    def banner(self, x, y, h=2.0, tile="cloth_red", w=0.32, l=0.7, facing=(0, 1)):
        self.log((x, y, 0), (x, y, h), r=0.045, sides=7)
        fx, fy = facing
        side = Vector((fy, -fx, 0)) if (fx, fy) != (0, 1) else Vector((1, 0, 0))
        top = Vector((x, y, h - 0.12)) + Vector((fx, fy, 0)) * 0.05
        self.log(top - side * (w / 2 + 0.05), top + side * (w / 2 + 0.05), r=0.02, sides=5)
        pts_t = [top - side * w / 2, top + side * w / 2]
        pts_b = [top - side * w / 2 - Vector((0, 0, l)), top + Vector((0, 0, -l * 1.15)), top + side * w / 2 - Vector((0, 0, l))]
        self.quad_strip([pts_b[0], pts_b[2]], pts_t, tile)

    def grindstone(self, x, y, z=0.0):
        self.box((x, y, z + 0.2), (0.3, 0.2, 0.4), tile="planks", bevel=0.01)
        self.log((x - 0.04, y, z + 0.52), (x + 0.04, y, z + 0.52), r=0.2, sides=14, taper=1, wobble=0, tile="stone", end_tile="stone")
        self.log((x - 0.2, y, z + 0.52), (x + 0.2, y, z + 0.52), r=0.02, sides=5, tile="iron")

    def anvil(self, x, y, z=0.0):
        self.log((x, y, z), (x, y, z + 0.35), r=0.14, sides=10)
        self.box((x, y, z + 0.42), (0.34, 0.14, 0.12), tile="iron", bevel=0.02, grain=0)
        self.box((x + 0.2, y, z + 0.44), (0.12, 0.08, 0.06), tile="iron", bevel=0.02, grain=0)

    def stone_pile(self, x, y, n=6, size=0.2, spread=0.3):
        for i in range(n):
            self.rock((x + self.rng.uniform(-spread, spread), y + self.rng.uniform(-spread, spread), size * 0.3 + (i // 4) * size * 0.4),
                      (size * self.rng.uniform(0.8, 1.3), size * self.rng.uniform(0.7, 1.1), size * self.rng.uniform(0.6, 0.9)))

    def cut_blocks(self, x, y, n=4, s=0.28):
        for i in range(n):
            row = i // 3
            self.box((x + (i % 3 - 1) * s * 1.05 + row * s * 0.5, y, s * 0.4 + row * s * 0.8), (s, s * 0.8, s * 0.8), (0, 0, self.rng.uniform(-6, 6)),
                     tile="stone", bevel=0.03, jitter=0.01)

    def merge(self, other, offset=(0, 0, 0)):
        """Adds another kit's parts, moved by offset."""
        o = Vector(offset)
        base = len(self.verts)
        self.verts.extend(v + o for v in other.verts)
        self.faces.extend([base + i for i in f] for f in other.faces)
        self.uvs.extend(other.uvs)
        self.smooth.extend(other.smooth)

    # MARK: - output

    def build(self, name):
        me = bpy.data.meshes.new(name)
        me.from_pydata([tuple(v) for v in self.verts], [], self.faces)
        uvl = me.uv_layers.new(name="UVMap")
        k = 0
        for poly, uv in zip(me.polygons, self.uvs):
            for li, (u, v) in zip(poly.loop_indices, uv):
                uvl.data[li].uv = (u, v)
        for poly, sm in zip(me.polygons, self.smooth):
            poly.use_smooth = sm
        me.validate(clean_customdata=False)
        me.update()
        obj = bpy.data.objects.new(name, me)
        bpy.context.scene.collection.objects.link(obj)
        return obj
