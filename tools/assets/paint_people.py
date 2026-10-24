#!/usr/bin/env python3
"""Paints the villagers' baked albedo (Person_<Name>_albedo.jpg) into the game's hand-painted look:
fine photo detail (cloth weave, pores, stubble) melts into flat painted areas with soft edges,
colours get a little richer and lighter, like the painted icons. Writes Person_<Name>_painted.jpg
next to each albedo and points people.json at it (the original stays the bake's source).

The dyed zones keep their grey level (the game multiplies them by colour / neutral grey), so the
per-villager dyes and skin tones still come out as people.json says.

Usage: python3 tools/assets/paint_people.py   (Python 3 with numpy, pillow and opencv-python-headless)
"""
import json
import os

import cv2
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
ASSETS = os.path.join(HERE, "..", "..", "unity", "Assets")
MANIFEST = os.path.join(ASSETS, "Art", "Resources", "people.json")

SATURATION = 1.08  # richer colours
LIFT = 1.04        # a touch lighter (the painted look has no dark photo grime)


def paint(img):
    # Edge-preserving smoothing, twice: flat colour areas, crisp borders between them.
    out = img
    for _ in range(3):
        out = cv2.bilateralFilter(out, 9, 38, 6)
    # A soft "brush" pass: mean shift merges near colours into painted patches.
    out = cv2.pyrMeanShiftFiltering(out, 5, 14)
    hsv = cv2.cvtColor(out, cv2.COLOR_BGR2HSV).astype(np.float32)
    hsv[..., 1] = np.clip(hsv[..., 1] * SATURATION, 0, 255)
    hsv[..., 2] = np.clip(hsv[..., 2] * LIFT, 0, 255)
    return cv2.cvtColor(hsv.astype(np.uint8), cv2.COLOR_HSV2BGR)


def main():
    data = json.load(open(MANIFEST))
    for p in data["people"]:
        t = p.get("textures")
        if not t:
            continue
        source = t.get("source") or t["albedo"]
        painted = source.replace("_albedo.jpg", "_painted.jpg")
        img = cv2.imread(os.path.join(ASSETS, source), cv2.IMREAD_COLOR)
        cv2.imwrite(os.path.join(ASSETS, painted), paint(img), [cv2.IMWRITE_JPEG_QUALITY, 92])
        t["source"], t["albedo"] = source, painted
        print("painted", painted)
    with open(MANIFEST, "w") as f:
        json.dump(data, f, indent=1)
        f.write("\n")


if __name__ == "__main__":
    main()
