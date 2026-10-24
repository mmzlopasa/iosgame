"""Draws "Green Hollow", a hand-composed valley in the spirit of Foundation, as the player's own map.

Writes unity/Assets/Custom/Resources/Custom/Map/Map.png (128 x 128, one pixel per 2 m cell, colours from
the Custom README) and Name.txt. The shapes are composed, not random: a lake with a wide sandy beach in the
south-west, a stream coming down from the mountains in the north-east, a long open meadow running up the
middle of the valley, big woods framing it with forest islands and glades, one great rock outcrop at the
edge of the eastern wood and two smaller ones, and ore along the foot of the mountains.

    python3 tools/map/foundation_valley.py [--out DIR] [--preview FILE]
"""
import argparse, math, os
import numpy as np
from PIL import Image

N = 128
COL = {'.': (110, 170, 70), 'T': (30, 90, 40), '~': (40, 110, 220), ',': (235, 215, 130),
       'R': (150, 150, 150), 'M': (80, 70, 65), 'O': (190, 90, 40), '*': (255, 0, 255)}

rng = np.random.default_rng(7)
ys, xs = np.mgrid[0:N, 0:N].astype(float)


def value_noise(scale, seed):
    """Smooth noise (bicubic-upsampled random grid) in 0..1."""
    r = np.random.default_rng(seed)
    g = int(math.ceil(N / scale)) + 3
    grid = r.random((g, g))
    img = Image.fromarray((grid * 255).astype(np.uint8)).resize((g * scale, g * scale), Image.BICUBIC)
    a = np.asarray(img, float)[scale:scale + N, scale:scale + N] / 255.0
    return a


def fbm(seed):
    return 0.55 * value_noise(32, seed) + 0.3 * value_noise(14, seed + 1) + 0.15 * value_noise(6, seed + 2)


def blob(cx, cy, rx, ry, wobble, seed, angle=0.0):
    """Signed distance-ish field of a wobbly ellipse (<0 inside), in cells."""
    ca, sa = math.cos(angle), math.sin(angle)
    dx, dy = xs - cx, ys - cy
    u, v = (dx * ca + dy * sa) / rx, (-dx * sa + dy * ca) / ry
    d = np.sqrt(u * u + v * v)
    n = value_noise(10, seed) - 0.5
    return (d - 1.0 - n * wobble) * min(rx, ry)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--out', default='unity/Assets/Custom/Resources/Custom/Map')
    ap.add_argument('--preview')
    ap.add_argument('--rows', help='also write the map as letters (Map.txt format)')
    args = ap.parse_args()

    g = np.full((N, N), '.', dtype='<U1')

    # --- Mountains along the north-east corner, with a spur down the east edge.
    dist_ne = np.sqrt((xs - 140) ** 2 + (ys + 14) ** 2)
    mnt = dist_ne < 46 + (fbm(11) - 0.5) * 16
    mnt |= (xs > 122 + (fbm(12) - 0.5) * 6) & (ys < 70)
    g[mnt] = 'M'

    # --- Woods: a frame around the valley, two great woods east and west, islands in the meadows.
    wood = fbm(21)
    edge = np.minimum.reduce([xs, ys, N - 1 - xs, N - 1 - ys])
    frame = np.clip((16 - edge) / 16, 0, 1)                    # 1 at the border, 0 inside
    # The long meadow: a soft corridor from the beach up the middle of the valley to the north.
    path = np.array([(44, 84), (56, 70), (60, 54), (56, 38), (50, 22), (46, 6)], float)
    def dist_to_path(p):
        best = np.full((N, N), 1e9)
        for (ax, ay), (bx, by) in zip(p[:-1], p[1:]):
            vx, vy = bx - ax, by - ay
            t = np.clip(((xs - ax) * vx + (ys - ay) * vy) / (vx * vx + vy * vy), 0, 1)
            best = np.minimum(best, np.hypot(xs - ax - t * vx, ys - ay - t * vy))
        return best
    meadow = dist_to_path(path)
    open_ = np.clip(1 - meadow / 27, 0, 1) ** 0.8              # 1 on the meadow's spine
    # A side meadow branching east towards the rock outcrop and a glade in the west wood.
    open_ = np.maximum(open_, np.clip(1 - dist_to_path(np.array([(60, 56), (78, 48), (92, 52)], float)) / 9, 0, 1))
    open_ = np.maximum(open_, np.clip(1 - np.hypot(xs - 20, ys - 30) / 11, 0, 1))
    open_ = np.maximum(open_, np.clip(1 - np.hypot(xs - 96, ys - 104) / 13, 0, 1))
    # Ragged wood edges: a finer noise lets the trees reach out and fall back along the meadow.
    ragged = value_noise(4, 23) - 0.5
    woodiness = 0.42 + 0.55 * wood + 0.6 * frame - 1.1 * open_ + 0.28 * ragged
    forest = woodiness > 0.62
    # Groves standing alone in the meadows (Foundation's islands of trees), a few cells across.
    for cx, cy, r, seed in [(40, 46, 3.2, 71), (66, 30, 2.6, 72), (58, 62, 2.2, 73), (36, 18, 2.8, 74), (74, 98, 3.0, 75), (100, 60, 2.4, 76)]:
        forest |= blob(cx, cy, r, r * 0.75, 0.6, seed, angle=seed * 0.7) < 0
    g[(g == '.') & forest] = 'T'

    # --- The lake with its beach (wide on the sunny north and east shore).
    lake = blob(32, 100, 23, 15, 0.35, 31, angle=-0.25)
    shore = np.arctan2(ys - 100, xs - 32)                       # -pi/2 = north
    width = 2.0 + 4.5 * np.clip(np.cos(shore + math.pi * 0.35), 0, 1)
    g[(lake > 0) & (lake < width) & (g != 'M')] = ','
    g[lake <= 0] = '~'

    # --- The stream: from the mountain foot down to the lake, winding, with a bit of gravel bank.
    pts = [(106, 30), (100, 40), (96, 52), (88, 62), (80, 74), (70, 80), (60, 88), (50, 94)]
    stream = np.full((N, N), 1e9)
    walked = 0.0
    for (ax, ay), (bx, by) in zip(pts[:-1], pts[1:]):
        seg = math.hypot(bx - ax, by - ay)
        nx, ny = -(by - ay) / seg, (bx - ax) / seg
        for t in np.linspace(0, 1, 60):
            # Meanders: the stream swings from side to side as it runs down.
            wob = math.sin((walked + seg * t) * 0.22) * 2.6
            px, py = ax + (bx - ax) * t + nx * wob, ay + (by - ay) * t + ny * wob
            stream = np.minimum(stream, np.hypot(xs - px, ys - py))
        walked += seg
    g[(stream < 1.6) & (g != 'M')] = '~'
    banks = (stream >= 1.6) & (stream < 2.6) & (value_noise(5, 41) > 0.55) & (g != 'M') & (g != '~')
    g[banks] = ','

    # --- Rock outcrops: one great one at the edge of the east wood, two small ones.
    for cx, cy, r, seed in [(86, 40, 6.5, 51), (24, 52, 4.0, 52), (104, 92, 3.6, 53), (64, 112, 3.0, 54)]:
        b = blob(cx, cy, r, r * 0.8, 0.5, seed, angle=0.6)
        g[(b < 0) & (g != '~') & (g != ',')] = 'R'

    # --- Ore along the foot of the mountains, in a few seams.
    m = g == 'M'
    pad = np.pad(m, 1)
    foot = np.zeros((N, N), bool)
    for dx in (-1, 0, 1):
        for dy in (-1, 0, 1):
            foot |= pad[1 + dy:1 + dy + N, 1 + dx:1 + dx + N]
    seams = value_noise(8, 61) > 0.45
    g[foot & ~m & seams & (g != '~')] = 'O'

    # --- Clean up: lone forest or meadow pixels join their neighbours (no salt and pepper).
    for _ in range(2):
        t = np.pad((g == 'T').astype(int), 1, mode='edge')
        cnt = sum(t[1 + dy:1 + dy + N, 1 + dx:1 + dx + N] for dx in (-1, 0, 1) for dy in (-1, 0, 1)) - t[1:-1, 1:-1]
        g[(g == 'T') & (cnt <= 2)] = '.'
        g[(g == '.') & (cnt >= 7)] = 'T'

    # --- Where to settle: open meadow near the beach, the wood and the stream.
    fx, fy = 50, 74
    img = np.zeros((N, N, 3), np.uint8)
    for k, c in COL.items():
        img[g == k] = c
    img[fy, fx] = COL['*']
    os.makedirs(args.out, exist_ok=True)
    Image.fromarray(img).save(os.path.join(args.out, 'Map.png'))
    with open(os.path.join(args.out, 'Name.txt'), 'w') as f:
        f.write('Green Hollow\n')
    if args.rows:
        rows = [''.join(r) for r in g]
        rows[fy] = rows[fy][:fx] + '*' + rows[fy][fx + 1:]
        open(args.rows, 'w').write('\n'.join(rows) + '\n')
    counts = {k: int((g == k).sum()) for k in COL if k != '*'}
    print('cells', counts, 'campfire', (fx, fy), 'cell there', g[fy, fx])
    if args.preview:
        Image.fromarray(img).resize((768, 768), Image.NEAREST).save(args.preview)


if __name__ == '__main__':
    main()
