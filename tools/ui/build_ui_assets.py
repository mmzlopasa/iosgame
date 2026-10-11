#!/usr/bin/env python3
"""Builds the UI assets for the Unity project (icons, logo, app icon, loading backdrop, fonts).

Everything is drawn as SVG/HTML and rasterised with headless Chromium, so the art stays
editable as text. Fonts (SIL OFL) are fetched from google/fonts and cut to static weights
with fontTools, because Unity's text engine reads the default instance of a variable font.

Usage:  python3 tools/ui/build_ui_assets.py            (needs: pip install fonttools pillow)
Output: unity/Assets/Art/UI/Resources/UI/{Icons,Images,Fonts}
"""
import glob
import os
import shutil
import subprocess
import sys
import tempfile
import urllib.request

from PIL import Image

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "unity", "Assets", "Art", "UI", "Resources", "UI")
LICENSES = os.path.join(ROOT, "unity", "Assets", "Art", "UI", "Licenses")

GOLD = ("#FFF0B8", "#FFD76A", "#E9B84A")

# 24×24 stroke icons (Lucide-style geometry, drawn white and tinted in Unity).
ICONS = {
    "food": ['<path d="M12 20.94c1.5 0 2.75 1.06 4 1.06 3 0 6-8 6-12.22A4.91 4.91 0 0 0 17 5c-2.22 0-4 1.44-5 2-1-.56-2.78-2-5-2a4.9 4.9 0 0 0-5 4.78C2 14 5 22 8 22c1.25 0 2.5-1.06 4-1.06Z"/>', '<path d="M10 2c1 .5 2 2 2 5"/>'],
    "wood": ['<path d="M3 8h13a3 3 0 0 1 0 6H3a3 3 0 0 1 0-6z"/>', '<path d="M16 8a3 3 0 0 1 0 6"/>', '<path d="M5 14v4h11v-4"/>'],
    "stone": ['<path d="M4 18l3-9 6-4 7 6-2 7z"/>', '<path d="M7 9l6 3 7-1"/>', '<path d="M13 12l-2 6"/>'],
    "knowledge": ['<path d="M9 18h6"/>', '<path d="M10 22h4"/>', '<path d="M12 2a7 7 0 0 0-4 12.7V17h8v-2.3A7 7 0 0 0 12 2z"/>'],
    "people": ['<path d="M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2"/>', '<circle cx="9" cy="7" r="4"/>', '<path d="M22 21v-2a4 4 0 0 0-3-3.87"/>', '<path d="M16 3.13a4 4 0 0 1 0 7.75"/>'],
    "user": ['<path d="M19 21v-2a4 4 0 0 0-4-4H9a4 4 0 0 0-4 4v2"/>', '<circle cx="12" cy="7" r="4"/>'],
    "build": ['<path d="m15 12-8.5 8.5a2.12 2.12 0 0 1-3-3L12 9"/>', '<path d="M17.64 15 22 10.64"/>', '<path d="m20.91 11.7-1.25-1.25c-.6-.6-.93-1.4-.93-2.25v-.86L16.01 4.6a5.56 5.56 0 0 0-3.94-1.64H9l.92.82A6.18 6.18 0 0 1 12 8.4v1.56l2 2h2.47l2.26 1.91"/>'],
    "goals": ['<circle cx="12" cy="12" r="10"/>', '<circle cx="12" cy="12" r="6"/>', '<circle cx="12" cy="12" r="2"/>'],
    "world": ['<path d="M3 6l6-3 6 3 6-3v15l-6 3-6-3-6 3z"/>', '<path d="M9 3v15"/>', '<path d="M15 6v15"/>'],
    "bell": ['<path d="M6 8a6 6 0 1 1 12 0c0 7 3 9 3 9H3s3-2 3-9"/>', '<path d="M10.3 21a1.94 1.94 0 0 0 3.4 0"/>'],
    "legacy": ['<path d="M4 19.5v-15A2.5 2.5 0 0 1 6.5 2H20v20H6.5a2.5 2.5 0 0 1 0-5H20"/>'],
    "settings": ['<path d="M12.22 2h-.44a2 2 0 0 0-2 2v.18a2 2 0 0 1-1 1.73l-.43.25a2 2 0 0 1-2 0l-.15-.08a2 2 0 0 0-2.73.73l-.22.38a2 2 0 0 0 .73 2.73l.15.1a2 2 0 0 1 1 1.72v.51a2 2 0 0 1-1 1.74l-.15.09a2 2 0 0 0-.73 2.73l.22.38a2 2 0 0 0 2.73.73l.15-.08a2 2 0 0 1 2 0l.43.25a2 2 0 0 1 1 1.73V20a2 2 0 0 0 2 2h.44a2 2 0 0 0 2-2v-.18a2 2 0 0 1 1-1.73l.43-.25a2 2 0 0 1 2 0l.15.08a2 2 0 0 0 2.73-.73l.22-.39a2 2 0 0 0-.73-2.73l-.15-.08a2 2 0 0 1-1-1.74v-.5a2 2 0 0 1 1-1.74l.15-.09a2 2 0 0 0 .73-2.73l-.22-.38a2 2 0 0 0-2.73-.73l-.15.08a2 2 0 0 1-2 0l-.43-.25a2 2 0 0 1-1-1.73V4a2 2 0 0 0-2-2z"/>', '<circle cx="12" cy="12" r="3"/>'],
    "back": ['<path d="M15 18l-6-6 6-6"/>'],
    "next": ['<path d="M9 18l6-6-6-6"/>'],
    "close": ['<path d="M18 6 6 18"/>', '<path d="m6 6 12 12"/>'],
    "check": ['<path d="M20 6 9 17l-5-5"/>'],
    "plus": ['<path d="M12 5v14"/>', '<path d="M5 12h14"/>'],
    "minus": ['<path d="M5 12h14"/>'],
    "clock": ['<circle cx="12" cy="12" r="10"/>', '<path d="M12 6v6l4 2"/>'],
    "lock": ['<rect x="3" y="11" width="18" height="11" rx="2"/>', '<path d="M7 11V7a5 5 0 0 1 10 0v4"/>'],
    "home": ['<path d="m3 9 9-7 9 7v11a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z"/>', '<path d="M9 22V12h6v10"/>'],
    "storage": ['<path d="M21 8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73l7 4a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16Z"/>', '<path d="m3.3 7 8.7 5 8.7-5"/>', '<path d="M12 22V12"/>'],
    "compass": ['<circle cx="12" cy="12" r="10"/>', '<path d="m16.24 7.76-2.12 6.36-6.36 2.12 2.12-6.36 6.36-2.12z"/>'],
    "sparkle": ['<path d="M12 3l1.9 5.8L20 10.7l-5.8 1.9L12 18.5l-1.9-5.9L4 10.7l6.1-1.9z"/>'],
    "alert": ['<path d="m21.73 18-8-14a2 2 0 0 0-3.48 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.73-3"/>', '<path d="M12 9v4"/>', '<path d="M12 17h.01"/>'],
    "flame": ['<path d="M8.5 14.5A2.5 2.5 0 0 0 11 12c0-1.38-.5-2-1-3-1.07-2.14-.22-4.05 2-6 .5 2.5 2 4.9 4 6.5 2 1.6 3 3.5 3 5.5a7 7 0 1 1-14 0c0-1.15.43-2.29 1-3a2.5 2.5 0 0 0 2.5 2.5z"/>'],
    "copy": ['<rect x="8" y="8" width="14" height="14" rx="2"/>', '<path d="M4 16c-1.1 0-2-.9-2-2V4c0-1.1.9-2 2-2h10c1.1 0 2 .9 2 2"/>'],
    "spring": ['<path d="M7 20h10"/>', '<path d="M10 20c5.5-2.5.8-6.4 3-10"/>', '<path d="M9.5 9.4c1.1.8 1.8 2.2 2.3 3.7-2 .4-3.5.4-4.8-.3-1.2-.6-2.3-1.9-3-4.2 2.8-.5 4.4 0 5.5.8z"/>', '<path d="M14.1 6a7 7 0 0 0-1.1 4c1.9-.1 3.3-.6 4.3-1.4 1-1 1.6-2.3 1.7-4.6-2.7.1-4 1-4.9 2z"/>'],
    "summer": ['<circle cx="12" cy="12" r="4"/>', '<path d="M12 2v2"/>', '<path d="M12 20v2"/>', '<path d="m4.93 4.93 1.41 1.41"/>', '<path d="m17.66 17.66 1.41 1.41"/>', '<path d="M2 12h2"/>', '<path d="M20 12h2"/>', '<path d="m6.34 17.66-1.41 1.41"/>', '<path d="m19.07 4.93-1.41 1.41"/>'],
    "autumn": ['<path d="M11 20A7 7 0 0 1 9.8 6.1C15.5 5 17 4.48 19 2c1 2 2 4.18 2 8 0 5.5-4.78 10-10 10Z"/>', '<path d="M2 21c0-3 1.85-5.36 5.08-6C9.5 14.52 12 13 13 12"/>'],
    "winter": ['<path d="M2 12h20"/>', '<path d="M12 2v20"/>', '<path d="m20 16-4-4 4-4"/>', '<path d="m4 8 4 4-4 4"/>', '<path d="m16 4-4 4-4-4"/>', '<path d="m8 20 4-4 4 4"/>'],
    "approval": ['<circle cx="12" cy="12" r="10"/>', '<path d="M8 14s1.5 2 4 2 4-2 4-2"/>', '<path d="M9 9h.01"/>', '<path d="M15 9h.01"/>'],
    "trophy": ['<path d="M6 9H4.5a2.5 2.5 0 0 1 0-5H6"/>', '<path d="M18 9h1.5a2.5 2.5 0 0 0 0-5H18"/>', '<path d="M4 22h16"/>', '<path d="M10 14.66V17c0 .55-.47.98-.97 1.21C7.85 18.75 7 20.24 7 22"/>', '<path d="M14 14.66V17c0 .55.47.98.97 1.21C16.15 18.75 17 20.24 17 22"/>', '<path d="M18 2H6v7a6 6 0 0 0 12 0V2Z"/>'],
}

LOGO = """
<svg xmlns="http://www.w3.org/2000/svg" width="{s}" height="{s}" viewBox="0 0 120 120">
  <defs><linearGradient id="g" x1="0" y1="0" x2="1" y2="1">
    <stop offset="0" stop-color="#FFF0B8"/><stop offset=".55" stop-color="#FFD76A"/><stop offset="1" stop-color="#E9B84A"/>
  </linearGradient></defs>
  <path d="M22 84 A38 38 0 0 1 98 84" fill="none" stroke="url(#g)" stroke-width="3.2" stroke-linecap="round"/>
  <path d="M60 30 V22 M33 41 L28 35 M87 41 L92 35 M22 62 L14 60 M98 62 L106 60" stroke="#FFD76A" stroke-width="3" stroke-linecap="round" opacity=".85"/>
  <ellipse cx="60" cy="88" rx="24" ry="9.5" fill="url(#g)"/>
  <ellipse cx="59" cy="72" rx="16.5" ry="7.8" fill="url(#g)"/>
  <ellipse cx="61" cy="58.5" rx="10" ry="6.2" fill="url(#g)"/>
  <path d="M18 98 H102" stroke="#FFD76A" stroke-width="2.4" stroke-linecap="round" opacity=".55"/>
</svg>"""

# Full-bleed square: iOS applies its own corner mask, so no rounding and no transparency.
APP_ICON = """
<div style="width:1024px;height:1024px;position:relative;overflow:hidden;
  background: radial-gradient(75% 60% at 50% 62%, rgba(255,200,90,.42), transparent 70%), linear-gradient(180deg,#1E2130 0%,#14161D 60%,#0D0F14 100%)">
<svg width="1024" height="1024" viewBox="0 0 120 120" style="position:absolute;left:0;top:0">
  <defs>
    <linearGradient id="g" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#FFF0B8"/><stop offset=".55" stop-color="#FFD76A"/><stop offset="1" stop-color="#E9B84A"/></linearGradient>
    <radialGradient id="sun" cx=".5" cy="1" r=".9"><stop offset="0" stop-color="#FFE7A0" stop-opacity=".55"/><stop offset="1" stop-color="#FFD76A" stop-opacity="0"/></radialGradient>
  </defs>
  <path d="M24 84 A36 36 0 0 1 96 84 Z" fill="url(#sun)"/>
  <path d="M0 92 L22 80 L38 88 L58 74 L78 86 L98 76 L120 88 V120 H0 Z" fill="#1A1D25"/>
  <path d="M0 100 L30 92 L60 98 L92 90 L120 96 V120 H0 Z" fill="#0F1117"/>
  <path d="M24 84 A36 36 0 0 1 96 84" fill="none" stroke="url(#g)" stroke-width="3" stroke-linecap="round"/>
  <path d="M60 36 V29 M35 46 L30 41 M85 46 L90 41 M25 65 L18 63 M95 65 L102 63" stroke="#FFD76A" stroke-width="2.6" stroke-linecap="round" opacity=".85"/>
  <ellipse cx="60" cy="88" rx="22" ry="8.6" fill="url(#g)"/>
  <ellipse cx="59" cy="73.5" rx="15" ry="7" fill="url(#g)"/>
  <ellipse cx="61" cy="61" rx="9.2" ry="5.6" fill="url(#g)"/>
</svg></div>"""

# Loading / launch backdrop (portrait, no text: the text is live UI so it stays sharp).
BACKDROP = """
<div style="width:1170px;height:2532px;position:relative;overflow:hidden;
  background: radial-gradient(70% 45% at 50% 34%, rgba(255,215,106,.22), transparent 70%),
              radial-gradient(60% 40% at 0% 100%, rgba(160,175,200,.14), transparent 60%),
              linear-gradient(180deg,#11131a 0%,#0D0F14 55%,#0a0b10 100%)">
<svg width="1170" height="900" viewBox="0 0 390 300" style="position:absolute;left:0;bottom:450px">
  <path d="M0 210 L40 170 L75 192 L120 128 L160 176 L205 120 L250 168 L292 132 L340 182 L390 150 L390 300 L0 300 Z" fill="#1A1D25"/>
  <path d="M0 238 L52 206 L98 230 L150 190 L196 226 L246 196 L300 232 L348 204 L390 222 L390 300 L0 300 Z" fill="#14161D"/>
  <path d="M0 266 L70 246 L132 262 L196 240 L262 260 L330 244 L390 258 L390 300 L0 300 Z" fill="#0F1117"/>
</svg>
<div style="position:absolute;left:0;right:0;bottom:0;height:452px;background:#0F1117"></div>
</div>"""

# Gold ring for the avatar (conic gradient, transparent centre).
RING = """
<div style="width:256px;height:256px;border-radius:50%;
  background: conic-gradient(from 210deg, #FFF0B8, #FFD76A, #E9B84A, #8F6A1E, #E9B84A, #FFF0B8);
  -webkit-mask: radial-gradient(circle, transparent 104px, #000 105px); mask: radial-gradient(circle, transparent 104px, #000 105px)"></div>"""


def chromium():
    hits = sorted(glob.glob("/opt/pw-browsers/chromium_headless_shell-*/chrome-linux/headless_shell"))
    if hits:
        return hits[-1]
    for name in ("chromium", "chromium-browser", "google-chrome"):
        path = shutil.which(name)
        if path:
            return path
    sys.exit("headless Chromium not found")


def render(html, width, height, out_png, transparent=True):
    with tempfile.TemporaryDirectory() as tmp:
        page = os.path.join(tmp, "page.html")
        with open(page, "w", encoding="utf-8") as f:
            f.write('<!doctype html><html><head><meta charset="utf-8"><style>html,body{margin:0;padding:0;background:transparent}</style></head><body>')
            f.write(html)
            f.write("</body></html>")
        shot = os.path.join(tmp, "shot.png")
        args = [chromium(), "--headless", "--no-sandbox", "--disable-gpu", "--hide-scrollbars",
                f"--window-size={width},{height}", f"--screenshot={shot}", "--force-device-scale-factor=1"]
        if transparent:
            args.append("--default-background-color=00000000")
        args.append("file://" + page)
        subprocess.run(args, check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        img = Image.open(shot)
        img = img.convert("RGBA") if transparent else img.convert("RGB")
        img.crop((0, 0, width, height)).save(out_png, optimize=True)


def build_icons():
    size, cell = 96, 112
    names = list(ICONS)
    cols = 8
    rows = (len(names) + cols - 1) // cols
    parts = []
    for i, name in enumerate(names):
        x, y = (i % cols) * cell, (i // cols) * cell
        body = "".join(ICONS[name])
        parts.append(f'<svg width="{size}" height="{size}" viewBox="0 0 24 24" fill="none" stroke="#fff" stroke-width="2" '
                     f'stroke-linecap="round" stroke-linejoin="round" style="position:absolute;left:{x}px;top:{y}px">{body}</svg>')
    W, H = cols * cell, rows * cell
    with tempfile.TemporaryDirectory() as tmp:
        sheet = os.path.join(tmp, "sheet.png")
        render('<div style="position:relative">' + "".join(parts) + "</div>", W, H, sheet)
        img = Image.open(sheet)
        out = os.path.join(OUT, "Icons")
        os.makedirs(out, exist_ok=True)
        for i, name in enumerate(names):
            x, y = (i % cols) * cell, (i // cols) * cell
            img.crop((x, y, x + size, y + size)).save(os.path.join(out, f"{name}.png"), optimize=True)
    print(f"icons: {len(names)}")


def build_images():
    out = os.path.join(OUT, "Images")
    os.makedirs(out, exist_ok=True)
    render(LOGO.format(s=512), 512, 512, os.path.join(out, "logo_mark.png"))
    render(BACKDROP, 1170, 2532, os.path.join(out, "loading_bg.png"), transparent=False)
    render(RING, 256, 256, os.path.join(out, "avatar_ring.png"))
    # The app icon lives outside Resources (it is only referenced by the build script).
    icon_dir = os.path.join(ROOT, "unity", "Assets", "Art", "UI", "AppIcon")
    os.makedirs(icon_dir, exist_ok=True)
    render(APP_ICON, 1024, 1024, os.path.join(icon_dir, "AppIcon.png"), transparent=False)
    # Launch screen: logo centred on the backdrop colour, sized for the iOS launch storyboard.
    print("images: logo_mark, loading_bg, avatar_ring, AppIcon")


FONTS = {
    "sora/Sora": [("SemiBold", 600), ("Bold", 700)],
    "geist/Geist": [("Regular", 400), ("Medium", 500), ("SemiBold", 600)],
    "geistmono/GeistMono": [("SemiBold", 600)],
}


def build_fonts():
    from fontTools.ttLib import TTFont
    from fontTools.varLib import instancer

    out = os.path.join(OUT, "Fonts")
    os.makedirs(out, exist_ok=True)
    os.makedirs(LICENSES, exist_ok=True)
    base = "https://raw.githubusercontent.com/google/fonts/main/ofl/"
    with tempfile.TemporaryDirectory() as tmp:
        for path, weights in FONTS.items():
            family = path.split("/")[1]
            src = os.path.join(tmp, family + ".ttf")
            urllib.request.urlretrieve(base + path + "%5Bwght%5D.ttf", src)
            urllib.request.urlretrieve(base + path.split("/")[0] + "/OFL.txt", os.path.join(LICENSES, f"OFL-{family}.txt"))
            for style, weight in weights:
                font = instancer.instantiateVariableFont(TTFont(src), {"wght": weight}, updateFontNames=True)
                font.save(os.path.join(out, f"{family}-{style}.ttf"))
    print("fonts: " + ", ".join(sorted(os.listdir(out))))


if __name__ == "__main__":
    build_icons()
    if "--icons-only" in sys.argv:
        sys.exit(0)
    build_images()
    if "--no-fonts" not in sys.argv:
        build_fonts()
