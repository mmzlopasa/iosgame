# Puts the baked tree pictures into the layout of the pack's Cards.png and bleeds their colour
# outwards (so mipmaps of the cut-out show no white or black fringe).
import json, glob, sys
import numpy as np
from PIL import Image
from scipy import ndimage
out_dir, mask_path, dest = sys.argv[1:4]
rects = {}
for f in glob.glob(out_dir + '/rects_*.json'): rects.update(json.load(open(f)))
W = H = 4096
rgb = np.zeros((H, W, 3), np.float32); alpha = np.zeros((H, W), np.float32)
for tree, rs in rects.items():
    for x0, y0, x1, y1, path in rs:
        im = np.asarray(Image.open(path).convert('RGBA').resize((x1 - x0, y1 - y0), Image.LANCZOS), np.float32) / 255
        a = im[..., 3]
        rgb[y0:y1, x0:x1] = np.where(a[..., None] > 0, im[..., :3], 0)
        alpha[y0:y1, x0:x1] = a
solid = alpha > 0.5
# Bleed: every transparent pixel takes the colour of the nearest solid one.
idx = ndimage.distance_transform_edt(~solid, return_distances=False, return_indices=True)
filled = rgb[idx[0], idx[1]]
img = np.dstack([filled, np.where(solid, 1.0, 0.0)])
Image.fromarray((img * 255).clip(0, 255).astype(np.uint8), 'RGBA').save(dest, optimize=True)
print('trees', len(rects), 'solid share', solid.mean())
