#!/usr/bin/env python3
"""HTML mocks of the main screens in both orientations, built from the same skins, icons, font, type scale
and measurements as the Unity UI (GameUi*.cs / UiKit.cs), and rendered with headless Chromium so the design
can be reviewed without Unity:

  l_*  landscape, 965 x 446 units (a 390-point iPhone at GameUi.PhoneShortSide = 446 units across, ~0.875 pt per
       unit; the notch on the left and the home indicator below);
  p_*  portrait, 446 x 965 units (the same phone turned: the Dynamic Island on top, the kit's phone layouts:
       resource bar on top, menu hexagons on the right, every card / strip / window / dialog a bottom sheet
       across about the lower half of the screen with the back arrow under it);
  sl_* / sp_*  a small phone (iPhone SE, 667 x 375 points: 762 x 428 units, no notch), landscape and portrait.

Text follows the game's surface rule (UiKit E.T): white Changa One with a dark outline and a hard drop on
wood, buttons, cells and capsules; plain dark ink of the same hue on light parchment (panel, section, slot).

Every page is also checked for text that overflows its box (clipped, spilling out of a button or a cell):
the report is printed after each page and the run exits non-zero when anything overflows.

Usage: python3 tools/ui/mock/build_mocks.py [out_dir] [background.png]
Writes tools/ui/mock/{l,p,sl,sp}_*.html (+ kit.css, bg.jpg) and PNGs (2x) into out_dir.
"""
import colorsys
import html as htmlmod
import math
import os
import re
import subprocess
import sys
import tempfile

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))
import build_ui_assets as kit  # noqa: E402

UI = "../../../unity/Assets/Art/UI/Resources/UI/"
W, H = 965, 446
# Insets of a notched iPhone in landscape (units): the notch side and the home indicator (47 / 21 pt).
SL, SR, SB, ST = 54, 54, 24, 0
ORIENT = "l"

# Every screen in panel units (GameUi.ReferenceFor): a regular phone is 446 units across its short side in either
# orientation; a small phone (375 pt: SE, mini) 428, so a unit never drops under 0.875 pt. Portrait: the Dynamic
# Island on top (59 pt) and the home indicator below (34 pt). The SE has no notch (20 pt status bar in portrait).
ORIENTATIONS = {"l": (965, 446, 54, 54, 24, 0), "p": (446, 965, 0, 0, 39, 67),
                "sl": (762, 428, 0, 0, 0, 0), "sp": (428, 762, 0, 0, 0, 23)}
# Points per unit of each screen (for the side-by-side physical comparison).
PT_PER_UNIT = {"l": 390 / 446, "p": 390 / 446, "sl": 375 / 428, "sp": 375 / 428}


def set_orient(o):
    global W, H, SL, SR, SB, ST, ORIENT
    W, H, SL, SR, SB, ST = ORIENTATIONS[o]
    ORIENT = o


def P():
    return ORIENT in ("p", "sp")


def bottom_pad():
    """GameUi.BottomPad: the home indicator's inset, or a little air on phones without one."""
    return max(10, SB)


BACK_ZONE = 60          # GameUi.BackZone: room under a portrait bottom sheet for the back arrow
PLAYER_H = 72           # the player block (hexagon frame) under the portrait bar

# MARK: - Type scale and metrics (mirror UiKit.Theme and the GameUi constants)

H1, H2, NUM, BODY, SMALL = 20, 17, 15, 14, 12
ROW_H, ROW_TIGHT, BUTTON_H, GO_H, TAB_H, PLANK_H, SECTION_H, CLOSE = 40, 36, 46, 34, 36, 38, 30, 38

STRIP_H = 38            # the top capsule
AVATAR = 46
GEAR = 42
CHIPS_H = 28
RAIL_Y = 86             # left rail (goal card) under the player block
TILES_Y = 54            # right column (tiles, timers) right under the strip
LEFT_RAIL_W = 224
TIMERS_W = 182
TIMER_H = 42
TILE = 48
TILE_GAP = 2
BUILD_BTN = 70
SMALL_BTN = 44
COMPASS = 56
POPUP_TOP = 50
GO_W = 68


def safe_w(narrow=False):
    return 693 if narrow else W - SL - SR


def sheet_w():
    """GameUi.SheetW: a portrait bottom sheet keeps the close button on its plank's corner inside the screen."""
    return min(560, safe_w() - 28)


def card_w(narrow=False):
    if P():
        return sheet_w()
    return min(360, safe_w(narrow) * 0.52)


def window_w(narrow=False):
    if P():
        return sheet_w()
    return max(560, min(680, safe_w(narrow) - 120))


def ow(size):
    """Outline width for outlined text (UiKit.Theme.OutlineFor)."""
    return max(0.8, min(1.7, size * 0.085))


def hexrgb(c):
    c = c.lstrip("#")
    if len(c) == 3:
        c = "".join(ch * 2 for ch in c)
    return tuple(int(c[i:i + 2], 16) / 255 for i in (0, 2, 4))


def on_parchment(color):
    """UiKit Theme.OnParchment: the ink colour a text colour becomes on light parchment."""
    r, g, b = hexrgb(color)
    h, s, v = colorsys.rgb_to_hsv(r, g, b)
    if s < 0.25:
        return "#3F2612" if v > 0.85 else "#5A3A1E"
    hue = h * 360
    target = 0.52 if 35 < hue < 75 else 0.6
    r, g, b = colorsys.hsv_to_rgb(h, max(0.7, min(1, s * 1.15)), min(v, target))
    return "#%02X%02X%02X" % (round(r * 255), round(g * 255), round(b * 255))


LIGHT = {"panel", "section", "slot"}

UP, BAD, GOLD, PALE, LV, ICY = "#B6F25A", "#FF6A5A", "#FFE07A", "#F3E6D2", "#D7F25A", "#A9DCFF"


def css():
    rules = ["@font-face{font-family:Changa;src:url(" + UI + "Fonts/ChangaOne-Regular.ttf)}",
             "*{box-sizing:border-box}",
             "body{margin:0;position:relative;overflow:hidden;font-family:Changa;background:#333 url(bg.jpg) center/cover}",
             ".a{position:absolute}.row{display:flex;flex-direction:row;align-items:center}.col{display:flex;flex-direction:column}",
             ".grow{flex:1 1 0;min-width:0}.c{justify-content:center;align-items:center}.nos>*{flex-shrink:0}",
             # Outlined text: colour, dark outline outside the glyph (paint-order) and a hard drop.
             ".o{color:var(--c);paint-order:stroke fill;-webkit-text-stroke-color:#3A2314;line-height:1.12;white-space:nowrap;flex:none}",
             ".o.w{white-space:normal;flex:0 1 auto;min-width:0}.o.flex{flex:1 1 0;min-width:0;white-space:normal}",
             # On parchment the same text is plain ink of the same hue; on wood inside parchment it is outlined again.
             ".light .o{color:var(--ci);-webkit-text-stroke-width:0 !important;text-shadow:0 1px 0 rgba(255,248,230,.55) !important}",
             ".light .dark .o{color:var(--c);-webkit-text-stroke-width:var(--sw) !important;text-shadow:0 var(--sd) 0 #3A2314 !important}",
             ".dark .light .o{color:var(--ci);-webkit-text-stroke-width:0 !important;text-shadow:0 1px 0 rgba(255,248,230,.55) !important}",
             ".ink{color:#5A3A1E;line-height:1.18}",
             ".ic{background-size:100% 100%;flex:none}"]
    for name, (_h, w, hgt, l, t, r, b) in kit.SKINS.items():
        s = kit.SKIN_SCALE
        mode = "round" if name in kit.TILED else "stretch"   # UiKit: tiled skins repeat (-unity-slice-type: tiled)
        rules.append(f".sk-{name}{{border-style:solid;border-width:{t}px {r}px {b}px {l}px;"
                     f"border-image:url({UI}Skin/{name}.png) {t * s} {r * s} {b * s} {l * s} fill / {t}px {r}px {b}px {l}px {mode}}}")
    return "\n".join(rules)


# MARK: - Components (same names and numbers as the C# helpers)

def t(text, size=BODY, color="#fff", extra="", cls=""):
    """E.T: one line unless cls has w (wraps) or flex (row label that wraps)."""
    w = ow(size)
    return (f'<div class="o {cls}" style="--c:{color};--ci:{on_parchment(color)};--sw:{2 * w:.2f}px;--sd:{w + 1:.2f}px;'
            f'font-size:{size}px;-webkit-text-stroke-width:{2 * w:.2f}px;padding:0 {math.ceil(w)}px;'
            f'text-shadow:0 {w + 1:.2f}px 0 #3A2314;{extra}">{text}</div>')


def ink(text, size=BODY, color="#5A3A1E", extra=""):
    return f'<div class="ink" style="font-size:{size}px;color:{color};{extra}">{text}</div>'


def icon(name, size, extra=""):
    return f'<div class="ic" style="width:{size}px;height:{size}px;background-image:url({UI}Icons/{name}.png);{extra}"></div>'


def box(name, inner, style="", cls="", pad="0"):
    """An element with a 9-slice skin drawn over its whole box (UITK draws the slices over the padding)."""
    surf = "light" if name in LIGHT else "dark"
    return (f'<div class="bx {surf} {cls}" style="position:relative;{style}">'
            f'<div class="a sk-{name}" style="inset:0"></div>'
            f'<div class="{cls}" style="position:relative;padding:{pad};height:100%;width:100%">{inner}</div></div>')


def subline(icon_name, text, color="#fff"):
    return f'<div class="row c" style="gap:3px;margin-top:-2px">{icon(icon_name, 16)}{t(text, 13, color)}</div>'


def btn(kind, label, w=None, h=BUTTON_H, sub=None, size=16, icon_name=None, share=False):
    sw = f"width:{w}px;" if w else ""
    if share:
        sw += "flex:1 1 0;min-width:0;"
    else:
        sw += "flex:none;"
    inner = f'<div class="row c" style="gap:5px">{icon(icon_name, size + 6) if icon_name else ""}{t(label.upper(), size)}</div>'
    if sub:
        inner += sub
    return box(f"btn_{kind}", f'<div class="col c" style="height:100%;padding:0 10px 4px">{inner}</div>', f"{sw}height:{h}px")


def sq(kind, icon_name, size=40):
    return box(f"btn_{kind}", f'<div class="col c" style="height:100%;padding-bottom:3px">{icon(icon_name, round(size * 0.6))}</div>',
               f"width:{size}px;height:{size}px;flex:none")


def tab(label, on, w=None, h=TAB_H, count=None, size=NUM, icon_name=None):
    h = h + 3 if on else h
    sw = f"width:{w}px;flex:none;" if w else "flex:1 1 0;"
    lab = (label or "") + (f' <span style="color:{LV}">{count}</span>' if count else "")
    inner = (icon(icon_name, 24) if icon_name else "") + (t(lab, size, "#fff" if on else PALE, "margin-left:4px" if icon_name else "") if label else "")
    return box("tab_on" if on else "tab_off", f'<div class="row c" style="height:100%;padding:0 10px {4 if on else 3}px">{inner}</div>',
               f"{sw}height:{h}px;align-self:flex-end")


def capsule(inner, h=32, style="", pad="0 11px", gap=12, grows=False):
    """E.Capsule: a fixed height (or a minimum when it may grow with wrapped text)."""
    return box("capsule", f'<div class="row" style="height:100%;padding:{pad};gap:{gap}px">{inner}</div>', f"{'min-height' if grows else 'height'}:{h}px;{style}")


def badge(text, kind="red", size=20):
    return box(f"badge_{kind}", f'<div class="row c" style="height:100%;padding:0 4px 1px">{t(text, max(SMALL, round(size * 0.66)))}</div>',
               f"min-width:{size}px;height:{size}px;flex:none")


def bar(frac, w="100%", h=12, kind="green", label=None, extra=""):
    fill = f'<div class="a sk-fill_{kind}" style="left:0;top:0;bottom:0;width:{max(frac * 100, 0)}%;min-width:{h}px"></div>' if frac > 0 else ""
    lab = f'<div class="a row c" style="inset:0">{t(label, max(SMALL, h - 4))}</div>' if label else ""
    return f'<div class="dark" style="position:relative;width:{w};height:{h}px;flex:none;{extra}"><div class="a sk-bar_track" style="inset:0"></div>{fill}{lab}</div>'


def plank(title, lv=None, size=H1, w="100%", extra=""):
    lvh = f' <span style="color:{LV}">Lv.{lv}</span>' if lv is not None else ""
    return box("plank", f'<div class="row c" style="height:100%;padding:0 40px 4px">{t(title + lvh, size)}</div>{extra}', f"width:{w};height:{PLANK_H}px;flex:none")


def section(title, rows, extra_head="", style=""):
    head = (f'<div class="row dark" style="min-height:{SECTION_H}px;padding:0 10px;margin:1.5px 1.5px 0;background:#D9A266;border-radius:8px 8px 0 0;border-bottom:1.5px solid #BF884E;gap:8px">'
            f'{t(title, H2, cls="flex")}{extra_head}</div>')
    return box("section", f'<div class="col">{head}<div class="col" style="padding:0 10px 4px">{"".join(rows)}</div></div>', f"flex:none;{style}")


def inset(inner, style="", pad="7px 10px", cls="col"):
    return box("section", inner, f"flex:none;{style}", cls, pad)


def line(content, sep=True, h=ROW_H, extra=""):
    return f'<div class="row" style="min-height:{h}px;padding:2px 0;{"border-top:1.5px solid #CFA878;" if sep else ""}{extra}">{content}</div>'


def rowicon(name):
    return icon(name, 24, "margin-right:8px")


def statrow(icon_name, label, now, nxt=None, sep=True, now_color="#fff", h=ROW_H):
    right = t(now, NUM, now_color, "margin-left:8px")
    if nxt is not None:
        right += icon("arrow", 20, "margin:0 6px") + t(nxt, NUM, UP)
    return line((rowicon(icon_name) if icon_name else "") + t(label, BODY, cls="flex") + right, sep, h)


def go():
    return btn("blue", "Go", GO_W, GO_H, size=NUM)


def rowend(content):
    return f'<div class="row c" style="width:{GO_W}px;margin-left:8px;flex:none">{content}</div>'


def reqrow(icon_name, content, met, sep=True, go_btn=True):
    end = icon("check", 28) if met else (go() if go_btn else icon("check", 28, "opacity:.45;filter:grayscale(1)"))
    return line(rowicon(icon_name) + f'<div class="row grow" style="flex-wrap:wrap">{content}</div>' + rowend(end), sep)


def costrow(icon_name, have, need, short, sep=True):
    content = t(have, NUM, BAD if short else UP) + t(" / " + need, NUM)
    end = go() if short else icon("check", 28)
    return line(rowicon(icon_name) + content + '<div class="grow"></div>' + rowend(end), sep)


def slot(icon_name, size=44, kind="slot", badge_text=None, lock=False, dim=False, share=None):
    b = f'<div class="a" style="left:-5px;bottom:-3px">{badge(badge_text, "green", 22)}</div>' if badge_text else ""
    lk = f'<div class="a" style="right:-5px;bottom:-4px">{icon("lock", 26)}</div>' if lock else ""
    op = "opacity:.55;filter:grayscale(.7);" if dim else ""
    share = share or (0.86 if icon_name and icon_name.startswith("face") else 0.66)
    sk = kind if kind in ("slot", "tile") else f"cell_{kind}"
    return box(sk, f'<div class="col c" style="height:100%;padding-bottom:3px;{op}">{icon(icon_name, round(size * share)) if icon_name else ""}</div>{b}{lk}',
               f"width:{size}px;height:{size}px;flex:none")


def reward(ic, kind, amount, size=48):
    """GameUi.RewardCell: a rarity cell, the icon high, the amount on the lower edge (may reach past the sides)."""
    return box(f"cell_{kind}", f'<div class="col" style="height:100%;align-items:center;padding-top:3px">{icon(ic, round(size * 0.58))}</div>'
               f'<div class="a row c" style="left:-6px;right:-6px;bottom:1px">{t(amount, SMALL)}</div>', f"width:{size}px;height:{size}px;flex:none")


def costline(items, size=BODY, center=False):
    out = "".join(f'<div class="row" style="margin:0 3px">{icon(i, size + 5)}{t(v, size, BAD if short else "#fff", "margin-left:1px")}</div>'
                  for i, v, short in items)
    return f'<div class="row" style="flex-wrap:wrap;{"justify-content:center;" if center else ""}">{out}</div>'


def notice(icon_name, text, bad=False, style=""):
    return inset(f'{icon(icon_name, 24)}{t(text, BODY, BAD if bad else "#fff", "margin-left:8px", "flex")}', style, cls="row")


# MARK: - HUD (the blue kit: hexagon player frame, navy counters, hexagon menu buttons, glossy blue squares)

HEX_FRAME_W, PLAYER_NAME_W = 64, 156
COUNTER_H, COUNTER_GAP, CHIP_H, ICON_ROOM = 30, 10, 28, 26
HUD_SQUARE = 40
TILE_COL_W = TILE + 16       # hexagon 48 + label overhang


def hud_counter(icon_name, text, plus=False, color="#fff", badge_html=""):
    pl = (f'<div class="bx dark" style="position:relative;width:24px;height:25.5px;margin:0 -3px 1px 6px;flex:none">'
          f'<div class="a sk-hud_plus" style="inset:0"></div></div>') if plus else ""
    inner = (f'<div class="a" style="left:-8px;top:-3px">{icon(icon_name, COUNTER_H + 6)}</div>{badge_html}'
             f'{t(text, NUM, color)}{pl}')
    return box("hud_capsule", f'<div class="row" style="height:100%;padding:0 {0 if plus else 12}px 0 {ICON_ROOM + 4}px">{inner}</div>',
               f"height:{COUNTER_H}px;margin-left:6px;flex:none")


def hud_chip(icon_name, text, color="#fff"):
    return box("hud_capsule", f'<div class="row" style="height:100%;padding:0 10px 0 7px">{icon(icon_name, CHIP_H - 6)}{t(text, BODY, color, "margin-left:4px")}</div>',
               f"height:{CHIP_H}px;flex:none")


def hud_square(icon_name, size=44):
    return box("hud_square_blue", f'<div class="col c" style="height:100%;padding-bottom:4px">{icon(icon_name, round(size * 0.6))}</div>',
               f"width:{size}px;height:{round(size * 1.06)}px;flex:none")


def hud_hex(icon_name, label, size=TILE, badge_text=None):
    bd = f'<div class="a" style="right:2px;top:-4px">{badge(badge_text, "red", 20)}</div>' if badge_text else ""
    return (f'<div class="bx dark" style="position:relative;width:{size + 16}px;height:58px;margin-bottom:2px;display:flex;flex-direction:column;align-items:center">'
            + box("hud_hex", f'<div class="col c" style="height:100%;padding-bottom:4px">{icon(icon_name, round(size * 0.66))}</div>', f"width:{size}px;height:{size * 1.01}px;flex:none")
            + f'<div class="a row c" style="left:-4px;right:-4px;bottom:0">{t(label, SMALL)}</div>{bd}</div>')


def hud_bar(icon_name, frac, label, w, h=24):
    return (f'<div class="dark" style="position:relative;width:{w}px;height:{h}px;flex:none"><div class="a sk-hud_bar_track" style="inset:0"></div>'
            f'<div class="a" style="left:25px;top:5px;right:4px;bottom:5px"><div class="a sk-hud_bar_fill_blue" style="left:0;top:0;bottom:0;width:{frac * 100}%;min-width:{h - 10}px"></div></div>'
            f'<div class="a row c" style="left:25px;right:4px;top:0;bottom:0">{t(label, SMALL)}</div>'
            f'<div class="a" style="left:-1px;top:-1px">{icon(icon_name, h + 2)}</div></div>')


def top_items(compact):
    """The counters and chips of the top bar (GameUi.TopCandidates): html, width and drop priority."""
    people_badge = f'<div class="a" style="left:14px;top:-6px">{badge("!", "red", 18)}</div>'
    pieces = {
        "people": hud_counter("people", "46/105", True, badge_html=people_badge),
        "food": hud_counter("food", "486", True), "wood": hud_counter("wood", "1.3K", True), "stone": hud_counter("stone", "958"),
        "knowledge": hud_counter("knowledge", "237"), "ore": hud_counter("ore", "120"), "hides": hud_counter("hides", "78"),
        "clock": hud_chip("summer", "12:01"), "temp": hud_chip("thermometer", "21°", "#FFD27A"), "discontent": hud_chip("discontent", "21", UP),
        "sick": hud_chip("sick", "2", "#FFB3A8"), "hope": hud_chip("approval", "68", GOLD),
    }

    def cw(text, plus):
        return 6 + ICON_ROOM + 4 + text_w(text + "0", NUM) + (27 if plus else 12)

    def chw(text):
        return 7 + CHIP_H - 6 + 4 + text_w(text + "0", BODY) + 10

    order = [("people", cw("46/105", True), 0), ("food", cw("486", True), 0), ("wood", cw("1.3K", True), 0), ("stone", cw("958", False), 2),
             ("knowledge", cw("237", False), 3), ("ore", cw("120", False), 8), ("hides", cw("78", False), 9), ("clock", chw("00:00"), 7),
             ("temp", chw("−00°"), 4), ("discontent", chw("00"), 5), ("sick", chw("2"), 1)]
    if compact:
        order.append(("hope", chw("100"), 6))
    return pieces, order


def gap():
    return 6 if P() else COUNTER_GAP


def pack(order, avail, rows_n):
    """GameUi.TopRows: greedy rows; while they do not fit, the most expendable item is dropped."""
    items = list(order)
    while True:
        rows, x, ok = [[]], 0, True
        for it in items:
            w = (gap() if x > 0 else 0) + it[1]
            if x + w > avail(len(rows) - 1):
                if len(rows) == rows_n:
                    ok = False
                    break
                rows.append([])
                x, w = 0, it[1]
            rows[-1].append(it)
            x += w
        if ok:
            return rows
        items.remove(max(items, key=lambda i: i[2]))


def rows_html(pieces, rows):
    return "".join(f'<div class="row" style="height:{COUNTER_H + 6}px;gap:{gap()}px">{"".join(pieces[i[0]] for i in r)}</div>' for r in rows)


def player_block():
    frame = box("hud_hex_frame", f'<div class="col c" style="height:100%;padding-bottom:3px">{icon("face_m1", round(HEX_FRAME_W * .74))}</div>'
                f'<div class="a" style="right:0;top:2px">{badge("!", "red", 20)}</div>', f"width:{HEX_FRAME_W}px;height:72px;flex:none")
    rank = box("hud_rank", f'<div class="row c" style="height:100%;padding:0 12px 1px">{t("Lv.9", BODY, "#FFD23A")}</div>', "height:22px;align-self:flex-start;margin-top:2px")
    return (f'{frame}<div class="col" style="width:{PLAYER_NAME_W}px;margin-left:8px;padding-top:2px;flex:none">{t("Riverside", H2)}{rank}'
            f'<div style="margin-top:6px">{hud_bar("approval", .68, "68/100", PLAYER_NAME_W)}</div></div>')


def goal_card():
    return box("panel", f'<div class="col" style="padding:7px 11px 11px"><div class="row">{icon("goals", 24)}'
               f'{t("Build a storage pit near the fire", BODY, extra="margin-left:6px;width:%dpx;min-height:32px;display:flex;align-items:center" % (LEFT_RAIL_W - 22 - 30), cls="w")}</div>'
               f'{bar(0.5, "100%", 10, extra="margin-top:5px")}</div>', f"width:{LEFT_RAIL_W}px")


def timer_cards():
    timers = ""
    for ic, name, tm, fr, k in (("knowledge", "Pottery", "1:12", .4, "hud_bar_fill_blue"), ("build", "Woodcutters' Camp", "0:27", .7, "fill_gold")):
        fill = f'<div class="a sk-{k}" style="left:0;top:0;bottom:0;width:{fr * 100}%;min-width:9px"></div>'
        timers += box("hud_capsule", f'<div class="row" style="padding:4px 12px 5px {ICON_ROOM + 6}px">'
                      f'<div class="a" style="left:-8px;top:2px">{icon(ic, 34)}</div><div class="col grow">{t(name, SMALL)}'
                      f'<div class="row" style="margin-top:1px"><div style="position:relative;flex:1 1 0;height:9px"><div class="a sk-bar_track" style="inset:0"></div>{fill}</div>'
                      f'{t(tm, SMALL, extra="margin-left:5px")}</div></div></div>', f"width:{TIMERS_W}px;min-height:40px;margin:0 0 6px 6px")
    return timers


def tiles_html():
    return "".join(hud_hex(i, lab, TILE, b) for i, lab, b in (("goals", "Goals", "!"), ("people", "People", "29"), ("knowledge", "Research", None), ("customs", "Customs", "!")))


def build_button():
    return box("btn_orange", f'<div class="col c" style="height:100%;padding-bottom:6px">{icon("build", 42)}{t("BUILD", NUM, extra="margin-top:-4px")}</div>',
               "width:72px;height:72px")


def news(maxw):
    return box("hud_capsule", f'<div class="row" style="padding:3px 14px 3px {ICON_ROOM + 4}px;min-height:30px"><div class="a" style="left:-8px;top:-1px">{icon("bell", 32)}</div>'
               f'{t("A new member joined the community", 13, cls="w")}</div>', f"max-width:{maxw}px;min-height:30px;margin-left:6px")


def small_buttons():
    return f'<div class="row" style="gap:8px;align-items:flex-end">{hud_square("compass", 50)}{"".join(hud_square(i, SMALL_BTN) for i in ("world", "legacy", "eye_off"))}</div>'


def hud(narrow=False, panel_open=False, card_open=False):
    if P():
        return hud_p(panel_open, card_open)
    sl = 0 if narrow else SL
    sr = 0 if narrow else SR
    sb = 10 if narrow else SB
    sw = safe_w(narrow)
    compact = panel_open or card_open
    pieces, order = top_items(compact)
    player_w = HEX_FRAME_W + 8 + PLAYER_NAME_W
    avail0 = sw - 16 - (0 if compact else player_w + 10) - HUD_SQUARE - 10
    avail1 = sw - 16 - player_w - 10 - TILE_COL_W - 10
    rows = pack(order, lambda r: avail0 if r == 0 else avail1, 1 if compact else 2)
    player = "" if compact else player_block()
    out = [f'<div class="a row" style="left:{sl + 8}px;right:{sr + 8}px;top:4px;align-items:flex-start">{player}'
           f'<div class="col" style="margin-left:{0 if compact else 10}px">{rows_html(pieces, rows)}</div><div class="grow"></div>{hud_square("settings", HUD_SQUARE)}</div>']
    if panel_open:
        return "".join(out)
    if not card_open:
        out.append(f'<div class="a" style="left:{sl + 8}px;top:{RAIL_Y}px">{goal_card()}</div>')
        out.append(f'<div class="a col" style="right:{sr + 8 + TILE_COL_W + 8}px;top:{RAIL_Y}px">{timer_cards()}</div>')
    out.append(f'<div class="a col" style="right:{sr + 8}px;top:52px;align-items:flex-end">{tiles_html()}</div>')
    out.append(f'<div class="a" style="right:{sr + 6}px;bottom:{sb + 4}px">{build_button()}</div>')
    if not card_open:
        out.append(f'<div class="a" style="left:{sl + 8}px;bottom:{sb + 4}px">{small_buttons()}</div>')
        left_edge = sl + 8 + 50 + 3 * (SMALL_BTN + 8) + 10
        right_edge = sr + 6 + 72 + 10
        room = min(380, sw - 16 - 50 - 3 * (SMALL_BTN + 8) - 20 - 72 - 6)
        out.append(f'<div class="a row c" style="left:{left_edge}px;right:{right_edge}px;bottom:{sb + 4 + 8}px">{news(room)}</div>')
    return "".join(out)


# MARK: - Portrait HUD (GameUi.LayoutHudPortrait / BuildTop / BuildBottomPortrait)

def p_rows(compact):
    pieces, order = top_items(compact)
    avail = safe_w() - 16
    return pieces, pack(order, lambda r: avail, 2)


def bar_bottom(compact):
    return ST + 4 + len(p_rows(compact)[1]) * (COUNTER_H + 6)


def popup_top(compact=True):
    return bar_bottom(compact) + 8 if P() else ST + 56


def sheet_bottom():
    return bottom_pad() + BACK_ZONE


def back_arrow():
    return f'<div class="a" style="left:{SL + 6}px;bottom:{bottom_pad() + 2}px">{icon("back", 54)}</div>'


def hud_p(panel_open=False, card_open=False):
    compact = panel_open or card_open
    pieces, rows = p_rows(compact)
    bb = ST + 4 + len(rows) * (COUNTER_H + 6)
    shade_h = bb + (14 if compact else PLAYER_H + 18)
    out = [f'<div class="a" style="left:0;right:0;top:0;height:{shade_h}px;background:linear-gradient(180deg,rgba(20,13,5,.55),rgba(20,13,5,0))"></div>']
    player = "" if compact else f'<div class="row" style="margin-top:4px;align-items:flex-start">{player_block()}</div>'
    out.append(f'<div class="a col" style="left:{SL + 8}px;right:{SR + 8}px;top:{ST + 4}px">'
               f'<div class="row" style="align-items:flex-start"><div class="col">{rows_html(pieces, rows)}</div></div>'
               f'{player}</div>')
    if compact:
        return "".join(out)
    rail = bb + 4 + PLAYER_H + 8
    out.append(f'<div class="a col" style="left:{SL + 8}px;top:{rail}px;align-items:flex-start">{goal_card()}'
               f'<div class="col" style="margin-top:8px">{timer_cards()}</div></div>')
    settings = f'<div class="row c" style="width:{TILE_COL_W}px;margin-bottom:8px">{hud_square("settings", HUD_SQUARE)}</div>'
    out.append(f'<div class="a col" style="right:{SR + 8}px;top:{bb + 4}px;align-items:flex-end">{settings}{tiles_html()}</div>')
    room = safe_w() - 16 - 72 - 10
    out.append(f'<div class="a row" style="left:{SL + 8}px;right:{SR + 6}px;bottom:{bottom_pad() + 4}px;align-items:flex-end">'
               f'<div class="col grow" style="align-items:flex-start"><div style="margin-bottom:10px">{news(room)}</div>{small_buttons()}</div>{build_button()}</div>')
    return "".join(out)


def toast(narrow=False):
    maxw = min(400, safe_w() - 16 - 12) if P() else 380
    n = box("hud_capsule", f'<div class="row" style="padding:6px 16px 7px {ICON_ROOM + 6}px;min-height:36px"><div class="a" style="left:-8px;top:1px">{icon("check", 34)}</div>'
            f'{t("A new member joined the community", BODY, cls="w")}</div>', f"max-width:{maxw}px;min-height:36px;margin-left:6px")
    if P():
        # Notices rise from above the bottom row (GameUi.LayoutHudPortrait).
        return f'<div class="a row c" style="left:{SL + 8}px;right:{SR + 8}px;bottom:{bottom_pad() + 4 + round(SMALL_BTN * 1.06) + 10 + 30 + 10}px">{n}</div>'
    sl = 0 if narrow else SL
    return f'<div class="a row c" style="left:{sl + 8 + LEFT_RAIL_W + 10}px;right:{(0 if narrow else SR) + 8 + TILE_COL_W + 8 + TIMERS_W + 10}px;top:{RAIL_Y}px">{n}</div>'


def page_hud(narrow=False):
    return hud(narrow) + toast(narrow), "width:693px" if narrow else ""


# MARK: - Docked card (the kit's building popup)

def card(title, lv, body, foot, w=None, narrow=False):
    w = w or card_w(narrow)
    sl = 0 if narrow else SL
    bottom = SB + 8
    maxh = H - POPUP_TOP - 4 - SB - 8   # LayoutHud: the card starts 4 below PopupTop (clear of the XP bar)
    back = ""
    if P():
        # A bottom sheet across the screen, at most about the lower half, the back arrow under it.
        top_limit = max(popup_top(True), round(H * 0.45))
        bottom = sheet_bottom()
        maxh = H - top_limit - bottom
        sl = SL + (safe_w() - w) / 2 - 8
        back = back_arrow()
    return back + (f'<div class="a" style="left:{sl + 8}px;bottom:{bottom}px;width:{w}px;max-height:{maxh}px;display:flex">'
            + box("panel", f'<div class="col" style="height:100%;max-height:{maxh}px">'
                  f'<div style="margin:-6px -14px 0">{plank(title, lv)}</div>'
                  f'<div class="a" style="right:-12px;top:-12px">{sq("red", "close", CLOSE)}</div>'
                  f'<div class="col nos" style="flex:1 1 auto;min-height:0;overflow:hidden;padding-top:8px">{body}</div>'
                  f'<div class="row" style="flex:none;padding-top:8px;gap:8px">{foot}</div></div>',
                  f"width:{w}px", pad="0 10px 11px") + '</div>')


def head(icon_name, kind, title, desc, title_color="#fff"):
    return (f'<div class="row" style="align-items:flex-start">{slot(icon_name, 60, kind)}<div class="col grow" style="margin-left:10px">'
            f'{t(title, NUM, title_color)}{ink(desc, SMALL, extra="margin-top:2px")}</div></div>')


def page_heart():
    stats = section("Upgrade Stats", [statrow("flame", "Warmth", "+12°", "+13°", False), statrow("compass", "Reach", "6 cells", "7"),
                                      statrow("storage", "Storage", "300", "325"), statrow("people", "Foraging places", "6", "7")],
                    t("Lv.9/30", NUM, LV), "margin-top:8px")
    res = section("Upgrade Resources", [reqrow("level_up", t("Timber House", BODY) + t(" Lv.4", BODY, LV), True, False),
                                        reqrow("knowledge", t("Research Pottery", BODY), False),
                                        costrow("wood", "1.3K", "1.9K", True), costrow("stone", "958", "600", False)],
                  t("Lv.10", NUM, LV), "margin-top:8px")
    body = head("campfire", "legendary", "Heart", "The heart of the camp. Elders gather here to share knowledge.") + stats + res
    foot = (sq("brown", "trash", BUTTON_H) + btn("blue", "Stoke", sub=subline("wood", "48"), share=True)
            + btn("orange", "Level up", sub=subline("clock", "4:00"), share=True))
    return hud(card_open=True) + card("Stone Campfire", 9, body, foot), ""


def page_workplace():
    stats = section("Upgrade Stats", [statrow("people", "Wood Gathering places", "4", "5", False), statrow("road", "Road", "+10% work", now_color=UP)],
                    t("Lv.3/10", NUM, LV), "margin-top:8px")
    cw = card_w()
    cell = math.floor((cw - 40) / 5)
    grid = "".join(f'<div class="col" style="align-items:center;width:{cell}px;flex:none">{slot(f, 48, "slot")}{t(n, SMALL, extra="margin-top:2px")}</div>'
                   for f, n in zip(["face_m1", "face_f1", "face_m2", "face_f3"], ["Aldo", "Mira", "Tomas", "Lena"]))
    grid += f'<div class="col" style="align-items:center;width:{cell}px;flex:none">{slot("lock", 48, "slot", dim=True, share=.6)}{t("Lv.4", SMALL, PALE, "margin-top:2px")}</div>'
    stepper = (f'<div class="row c" style="margin:6px 0">{sq("red", "minus", 40)}'
               + capsule(f'{icon("user", 22)}{t("4/4", NUM, extra="margin-left:5px")}', 32, "margin:0 -3px", pad="0 14px", gap=0)
               + f'{sq("grey", "plus", 40)}</div>')
    workers = section("Wood Gathering", [f'<div class="row" style="padding-top:8px;align-items:flex-start">{grid}</div>',
                                         t("Wood 1.1/m each · 4.4/m total", SMALL, PALE, "margin-top:4px", "w"), stepper],
                      t("4/4", NUM, LV), "margin-top:8px")
    up = section("Upgrade Resources", [reqrow("campfire", t("Great Hearth level 30/4", BODY), True, False),
                                       costrow("wood", "1.3K", "500", False), costrow("stone", "120", "300", True)],
                 t("Lv.4", NUM, LV), "margin-top:8px")
    body = head("thumb_axe", "common", "Work", "Cuts wood from the trees around it. Faster next to a road.") + stats + workers + up
    body = body.replace("thumb_axe", "axe")
    foot = sq("brown", "trash", BUTTON_H) + btn("grey", "Level up", sub=subline("stone", "300", BAD), share=True)
    return hud(card_open=True) + card("Woodcutters' Camp", 3, body, foot), ""


# MARK: - Build strip

def page_build():
    tabs = "".join(tab(l if i == 0 else None, i == 0, None, TAB_H, None, NUM, ic).replace("flex:1 1 0;", "flex:none;min-width:48px;margin-right:4px;")
                   for i, (ic, l) in enumerate((("build", "All"), ("home", ""), ("food", ""), ("pickaxe", ""), ("storage", ""), ("road", ""),
                                                ("fence", ""), ("lantern", ""), ("tree", ""), ("landmark", ""))))
    items = [("home", "Shelter", "rare", [("wood", "12", False)], "×4", False),
             ("food", "Foraging Camp", "uncommon", [("wood", "10", False)], "×1", False),
             ("axe", "Wood Gatherers' Camp", "common", [("wood", "10", False)], "×1", False),
             ("stone", "Stone Gathering Site", "common", [("wood", "15", False)], "×1", False),
             ("storage", "Storage Pit", "legendary", [("wood", "15", False), ("stone", "8", False)], "×1", False),
             ("sick", "Healer's Hut", "rare", [("wood", "30", False), ("stone", "10", False)], None, False),
             ("landmark", "Community Circle", "common", None, "1/1", False),
             ("fish", "Fishing Spot", "uncommon", [("wood", "25", True)], None, True)]
    cards = ""
    item_w, cell = 104, 78
    for ic, name, rar, cst, cnt, locked in items:
        if cst is None:
            under = t("Limit reached", SMALL, PALE)
        elif locked:
            under = t("Fishing Lines", SMALL, "#E6C3FF")
        else:
            under = costline(cst, 13, True)
        nm_size = two_lines_size(name, item_w - 2, BODY)
        cards += (f'<div class="col bx" style="width:{item_w}px;align-items:center;flex:none;margin-right:6px">'
                  f'{slot(ic, cell, "common" if locked else rar, cnt, locked, dim=locked)}'
                  f'<div class="row c" style="width:{item_w - 2}px;min-height:{nm_size * 2.3:.1f}px;margin-top:3px;text-align:center">{t(name, nm_size, cls="w")}</div>'
                  f'{under}</div>')
    if P():
        return page_build_p(tabs, items)
    plank_html = box("plank", f'<div class="row c" style="height:100%;padding:0 16px 4px">{t("Build", H1)}</div>', f"width:176px;height:{PLANK_H}px;flex:none")
    strip = (f'<div class="a" style="left:{SL + 8}px;right:{SR + 8}px;bottom:{SB + 4}px;height:204px">'
             + box("panel", f'<div class="col" style="height:100%">'
                   f'<div class="row" style="margin:-6px 0 0 -14px;align-items:flex-end">{plank_html}'
                   f'<div class="row" style="margin-left:8px;align-items:flex-end;overflow:hidden;flex:1 1 0">{tabs}</div>'
                   + capsule(icon("build", 22) + t("0/2", NUM, extra="margin-left:4px"), 32, f"margin:0 {CLOSE}px 2px 8px", pad="0 10px", gap=0)
                   + f'</div><div class="a" style="right:-12px;top:-12px">{sq("red", "close", CLOSE)}</div>'
                   f'<div class="row" style="padding-top:10px;align-items:flex-start;overflow:hidden">{cards}</div></div>',
                   "width:100%;height:100%", pad="0 10px 10px") + "</div>")
    return hud(panel_open=True) + strip, ""


def item_card(ic, name, rar, cst, cnt, locked, margin):
    item_w, cell = 104, 78
    if cst is None:
        under = t("Limit reached", SMALL, PALE)
    elif locked:
        under = t("Fishing Lines", SMALL, "#E6C3FF")
    else:
        under = costline(cst, 13, True)
    nm_size = two_lines_size(name, item_w - 2, BODY)
    return (f'<div class="col bx" style="width:{item_w}px;align-items:center;flex:none;{margin}">'
            f'{slot(ic, cell, "common" if locked else rar, cnt, locked, dim=locked)}'
            f'<div class="row c" style="width:{item_w - 2}px;min-height:{nm_size * 2.3:.1f}px;margin-top:3px;text-align:center">{t(name, nm_size, cls="w")}</div>'
            f'{under}</div>')


def page_build_p(tabs, items):
    """GameUi.BuildDrawerPortrait: a bottom sheet over about the lower half: plank, tabs and queue, items in rows."""
    w = card_w()
    top = max(popup_top(True), round(H * 0.48))
    row_w = w - 20
    per = max(1, int(row_w // 104))
    side = math.floor((row_w - per * 104) / (2 * per))
    cards = "".join(item_card(*it, margin=f"margin:0 {side}px 10px") for it in items)
    sheet = (f'<div class="a" style="left:{SL + (safe_w() - w) / 2}px;width:{w}px;top:{top}px;bottom:{sheet_bottom()}px">'
             + box("panel", f'<div class="col" style="height:100%">'
                   f'<div style="margin:-6px -14px 0;flex:none">{plank("Build")}</div>'
                   f'<div class="a" style="right:-12px;top:-12px">{sq("red", "close", CLOSE)}</div>'
                   f'<div class="row" style="margin-top:6px;align-items:flex-end;flex:none"><div class="row" style="align-items:flex-end;overflow:hidden;flex:1 1 0">{tabs}</div>'
                   + capsule(icon("build", 22) + t("0/2", NUM, extra="margin-left:4px"), 32, "margin:0 0 2px 6px;flex:none", pad="0 10px", gap=0)
                   + f'</div><div class="row" style="flex-wrap:wrap;padding-top:10px;align-items:flex-start;overflow:hidden;flex:1 1 0;min-height:0;align-content:flex-start">{cards}</div></div>',
                   "width:100%;height:100%", pad="0 10px 10px") + "</div>")
    return hud(panel_open=True) + sheet + back_arrow(), ""


ADV = None


def adv(ch):
    global ADV
    if ADV is None:
        src = open(os.path.join(HERE, "../../../unity/Assets/Scripts/UI/UiGenerated.cs")).read()
        nums = re.search(r"Ascii = \{([^}]*)\}", src).group(1)
        ADV = [float(x.strip().rstrip("f")) for x in nums.split(",") if x.strip()]
    o = ord(ch)
    if 32 <= o < 127:
        return ADV[o - 32]
    return {"·": .277, "−": .6, "×": .6, "°": .407, "–": .595, "—": .854, "’": .275}.get(ch, .6)


def text_w(s, size, outlined=True):
    s = re.sub(r"<[^>]*>", "", s)
    return sum(adv(c) for c in s) * size + (2 * math.ceil(ow(size)) if outlined else 0) + 1


def wrapped_lines(s, size, width):
    room = width * 0.95 - 2 * math.ceil(ow(size)) - 1
    space = adv(" ") * size
    lines, x = 1, 0.0
    for word in s.split(" "):
        w = text_w(word, size, False) - 1
        if x > 0 and x + space + w > room:
            lines += 1
            x = w
        else:
            x += (space if x > 0 else 0) + w
        if w > room:
            lines += 1
    return lines


def two_lines_size(s, width, size, mn=SMALL):
    """UiKit Theme.TwoLineSize: the largest size at which the text wraps into two lines of the width."""
    f = size
    while f > mn:
        if wrapped_lines(s, f, width) <= 2:
            return f
        f -= 0.25
    return mn


# MARK: - Windows

def window(title, body, tabs=None, foot=None, bottom=None, back=True, narrow=False, top=None):
    w = window_w(narrow)
    sl = 0 if narrow else SL
    sw = safe_w(narrow)
    bottom = (sheet_bottom() if P() else SB + 8) if bottom is None else bottom
    left = sl + (sw - w) / 2
    if top is None:
        # Portrait: a bottom sheet over about the lower half (GameUi.WindowTopPortrait).
        top = max(popup_top(True), round(H * 0.40)) if P() else POPUP_TOP
    tabs_html = f'<div class="row" style="gap:6px;margin-top:6px;align-items:flex-end;flex:none">{tabs}</div>' if tabs else ""
    foot_html = f'<div class="row c" style="gap:16px;padding-top:8px;flex:none">{foot}</div>' if foot else ""
    win = (f'<div class="a" style="left:{left}px;top:{top}px;width:{w}px;bottom:{bottom}px">'
           + box("panel", f'<div class="col" style="height:100%">'
                 f'<div style="margin:-6px -14px 0;flex:none">{plank(title)}</div>'
                 f'<div class="a" style="right:-12px;top:-12px">{sq("red", "close", CLOSE)}</div>'
                 f'{tabs_html}<div class="col nos" style="flex:1 1 0;min-height:0;overflow:hidden;padding-top:8px">{body}</div>{foot_html}</div>',
                 "width:100%;height:100%", pad="0 10px 12px") + "</div>")
    if back:
        win += back_arrow() if P() else f'<div class="a" style="left:{sl + 4}px;bottom:{SB}px">{icon("back", 54)}</div>'
    return win


def inner_w(narrow=False):
    return window_w(narrow) - 20


def page_people():
    tabs = tab("Work", False, count="33 idle") + tab("Villagers", True, count="46") + tab("Wellbeing", False)
    people = [("face_m1", "Aldo", "Wood Gathering", "Woodcutter", 4, .6, False), ("face_f1", "Mira", "Healing", "Healer", 6, .2, True),
              ("face_m2", "Tomas", "Construction", "Builder", 3, .8, False), ("face_f3", "Lena", "Farming", "Farmer", 2, .4, False),
              ("face_m3", "Joss", "Idle", "Forager", 1, .1, False), ("face_f2", "Ines", "Research", "Thinker", 5, .5, False)]
    cell = inner_w() if P() and inner_w() < 480 else math.floor((inner_w() - 8) / 2)
    skill_w = 112
    text_wd = cell - 20 - 48 - 10 - skill_w - 6
    rows = ""
    for f, n, job, skill, lv, fr, sick in people:
        extra = f'<div class="a" style="right:-5px;bottom:-3px">{icon("sick", 20)}</div>' if sick else ""
        sick_line = f'{t("Ill · under care", SMALL, UP)}{bar(.4, "%dpx" % min(text_wd, 90), 8, extra="margin-top:3px")}' if sick else ""
        rows += inset(f'<div style="position:relative">{slot(f, 48, "slot")}{extra}</div>'
                      f'<div class="col grow" style="margin-left:10px;max-width:{text_wd}px">{t(n, NUM)}{t(job, SMALL, GOLD if job == "Idle" else PALE, cls="w")}{sick_line}</div>'
                      f'<div class="col" style="align-items:flex-end;width:{skill_w}px;margin-left:6px;flex:none">{t(skill, SMALL)}{t(f"Lv.{lv}", NUM, LV)}{bar(fr, "90px", 10, "gold", extra="margin-top:2px")}</div>',
                      f"width:{cell}px;margin-bottom:8px;min-height:64px", "8px 10px", "row")
    body = f'<div class="row" style="flex-wrap:wrap;justify-content:space-between">{rows}</div>'
    return hud(panel_open=True) + window("People & Work", body, tabs), ""


def page_people_work():
    tabs = tab("Work", True, count="33 idle") + tab("Villagers", False, count="46") + tab("Wellbeing", False)
    isz, gap, pad = (26, 6, "7px 8px") if P() else (32, 8, "7px 10px")
    stats = "".join(inset(f'{icon(i, isz)}<div class="col grow" style="margin-left:{gap}px">{t(v, H2, c)}{t(l, SMALL, PALE, cls="w" if P() else "")}{bar(.45, "100%", 8, extra="margin-top:3px") if i == "people" else ""}</div>', "flex:1 1 0;min-height:58px", pad, cls="row")
                    for i, v, l, c in (("people", "46/105", "People / beds", "#fff"), ("user", "33", "Idle", GOLD), ("food", "+4.2/m", "Food", UP)))
    jobs = ""
    for i, (ic, name, eff, w, cap) in enumerate((("food", "Foraging", "Food 1.4/m each · 8.4/m total", 6, 8), ("axe", "Wood Gathering", "Wood 1.1/m each · 5.5/m total", 5, 5),
                                                ("stone", "Stone Gathering", "Stone 0.8/m each · 2.4/m total", 3, 4), ("build", "Construction", "Build speed ×2.0", 4, 43))):
        can_add = w < cap
        count_w = 64 if P() else 80
        text_wd = inner_w() - 20 - 44 - 10 - (40 + count_w + 40 - 6 + 8)
        eff_size = max(SMALL, min(SMALL, SMALL))
        eff_html = t(eff, SMALL, PALE, cls="w" if text_w(eff, SMALL) > text_wd else "")
        jobs += line(f'{slot(ic, 44, "slot", share=.7)}<div class="col grow" style="margin-left:10px;max-width:{text_wd}px">{t(name, NUM)}{eff_html}</div>'
                     f'<div style="margin-left:8px">{sq("red", "minus", 40)}</div>'
                     + capsule(f'<div class="row c grow">{t(f"{w}/{cap}", NUM)}</div>', 32, f"width:{count_w}px;margin:0 -3px", pad="0 6px", gap=0)
                     + sq("green" if can_add else "grey", "plus", 40), i > 0, 58)
    body = (f'<div class="row" style="gap:8px;margin-bottom:8px">{stats}</div>'
            + section("Work", [jobs], t("Places from workplaces", SMALL, PALE)))
    return hud(panel_open=True) + window("People & Work", body, tabs), ""


def page_research():
    know = capsule(icon("knowledge", 24) + t("238  +12.6/m", NUM, extra="margin-left:5px"), 34, gap=0)
    tabs = f'<div class="grow">{t("23/23 discovered · Great Hearth Lv.30", BODY, PALE)}</div>' + know
    col_w, detail_w = 162, 240
    cols = ""
    for headtxt, techs in (("Level 1–5", [("Improved Stone Tools", "done"), ("Food Preservation", "done"), ("Herbal Healing", "done"), ("Structural Weaving", "done")]),
                           ("Level 6–10", [("Permanent Settlement", "done"), ("Oral Tradition", "avail"), ("Longhouses", "lock")]),
                           ("Level 11–15", [("Agriculture", "prog"), ("Animal Husbandry", "lock"), ("Pottery", "lock")])):
        nodes = ""
        for name, st in techs:
            kind = {"done": "uncommon", "avail": "legendary", "prog": "rare", "lock": "common"}[st]
            ic = {"done": "check", "avail": "knowledge", "prog": "clock", "lock": "lock"}[st]
            sel = name == "Oral Tradition"
            nm_w = col_w - 12 - 40 - 6
            size = two_lines_size(name, nm_w, BODY)
            prog = f'<div class="a" style="left:8px;right:8px;bottom:5px">{bar(.4, "100%", 7, "blue")}</div>' if st == "prog" else ""
            nodes += box("slot" if sel else "section", f'<div class="row" style="height:100%;min-height:56px;padding:0 6px {3 if sel else 0}px">{slot(ic, 40, kind, share=.62)}'
                         f'<div class="row" style="width:{nm_w}px;min-height:{size * 2.3:.1f}px;margin-left:6px">{t(name, size, PALE if st == "lock" else "#fff", cls="w")}</div></div>{prog}',
                         "min-height:56px;margin-bottom:8px")
        cols += (f'<div class="col" style="width:{col_w}px;flex:none;margin-right:8px"><div class="row c" style="height:28px">'
                 f'{icon("flame", 22)}{t(headtxt, NUM, extra="margin-left:5px")}</div>{nodes}</div>')
    if P():
        detail_w = inner_w()
        tabs = f'<div class="grow">{t("23/23 discovered", BODY, PALE)}</div>' + know
    detail = inset(f'<div class="row">{slot("knowledge", 52, "legendary", share=.62)}<div class="col grow" style="margin-left:8px">'
                   f'{t("Oral Tradition", H2)}<div class="row">{icon("clock", 20)}{t("4:00", NUM, extra="margin-left:4px")}</div></div></div>'
                   f'{ink("Stories by the fire keep what the elders know.", BODY, extra="margin-top:6px")}'
                   f'{t("+15% Research · unlocks Storyteller", BODY, UP, "margin-top:6px", "w")}'
                   f'<div style="margin-top:8px">{costrow("knowledge", "238", "120", False, False)}</div>'
                   + btn("green", "Research", detail_w - 20, sub=subline("clock", "4:00")).replace("flex:none", "flex:none;margin-top:8px"),
                   f"width:{detail_w}px", "10px 10px")
    if P():
        # GameUi.BuildResearchPortrait: the chosen technology on top, then the bands, two per row.
        node_w = math.floor((inner_w() - 8) / 2)
        bands = ""
        for headtxt, techs in (("Level 1–5", [("Improved Stone Tools", "done"), ("Food Preservation", "done"), ("Herbal Healing", "done")]),
                               ("Level 6–10", [("Permanent Settlement", "done"), ("Oral Tradition", "avail"), ("Longhouses", "lock")])):
            nodes = ""
            for i, (name, st) in enumerate(techs):
                kind = {"done": "uncommon", "avail": "legendary", "prog": "rare", "lock": "common"}[st]
                ic = {"done": "check", "avail": "knowledge", "prog": "clock", "lock": "lock"}[st]
                sel = name == "Oral Tradition"
                nm_w = node_w - 12 - 40 - 6
                size = two_lines_size(name, nm_w, BODY)
                nodes += box("slot" if sel else "section", f'<div class="row" style="height:100%;min-height:56px;padding:0 6px {3 if sel else 0}px">{slot(ic, 40, kind, share=.62)}'
                             f'<div class="row" style="width:{nm_w}px;min-height:{size * 2.3:.1f}px;margin-left:6px">{t(name, size, PALE if st == "lock" else "#fff", cls="w")}</div></div>',
                             f"width:{node_w}px;min-height:56px;margin-bottom:8px;margin-left:{8 if i % 2 else 0}px;flex:none")
            bands += (f'<div class="row c" style="height:30px;flex:none">{icon("flame", 22)}{t(headtxt, NUM, extra="margin-left:5px")}</div>'
                      f'<div class="row" style="flex-wrap:wrap;align-items:flex-start">{nodes}</div>')
        bar_html = capsule(icon("clock", 28) + f'<div class="col grow" style="margin-left:8px">{t("Agriculture", BODY)}<div class="row" style="margin-top:2px"><div style="flex:1 1 0;min-width:0">{bar(.4, "100%", 10, "blue")}</div>{t("2:41", SMALL, extra="margin-left:6px")}</div></div>',
                           46, "margin-bottom:8px;flex:none", pad="4px 12px 4px 8px", gap=0, grows=True)
        body = f'<div class="col">{bar_html}<div style="margin-bottom:8px">{detail}</div>{bands}</div>'
        return hud(panel_open=True) + window("Research", body, tabs), ""
    body = f'<div class="row" style="align-items:flex-start;height:100%"><div class="row grow" style="align-items:flex-start;overflow:hidden">{cols}</div><div style="margin-left:8px;flex:none">{detail}</div></div>'
    return hud(panel_open=True) + window("Research", body, tabs), ""


def page_goals():
    iw = inner_w()
    story_w = 120
    art = 48 if P() else 60
    text_wd = iw - 24 - 4 - art - 12 - (0 if P() else story_w + 10)
    story = btn("blue", "Story", story_w, 40 if P() else BUTTON_H, None, 16, "legacy")
    banner = (f'<div class="row dark" style="min-height:92px;margin-bottom:8px;border-radius:10px;border:2.5px solid #3A2314;padding:10px 12px;'
              f'background:linear-gradient(90deg,#4A2E1A,#8A5A32)">{icon("campfire", art)}'
              f'<div class="col" style="margin-left:12px;width:{text_wd}px;flex:none">{t("Chapter 6 · Middle Ages", BODY, GOLD)}{t("The Market Town", H1)}'
              f'{t("A market hall, a smithy and a watermill in a town of 60.", SMALL, PALE, "margin-top:2px", "w")}'
              + (f'<div style="margin-top:8px;align-self:flex-start">{story}</div>' if P() else "")
              + f'</div><div class="grow"></div>{"" if P() else story}</div>')
    tasks = ""
    for title, done in (("Reach The Market Town", False), ("Build a Watermill", True)):
        right = icon("check", 32) if done else btn("blue", "Go", 86, 42, None, 18)
        pill = capsule(t("1/1" if done else "0/1", NUM, UP if done else "#fff"), 30, "margin-left:8px", pad="0 12px")
        tasks += inset(f'{t(title, NUM, UP if done else "#fff", cls="flex")}{pill}<div class="row c" style="width:96px;flex:none">{right}</div>',
                       "min-height:58px;margin-bottom:8px", cls="row")
    road_rows = [line(rowicon(ic) + t(n, BODY, c, cls="flex") + t(e, SMALL, PALE, "margin-left:8px"), i > 0)
                 for i, (ic, n, c, e) in enumerate((("check", "1. The Riverside Camp", "#fff", "Stone Age"), ("check", "2. The Stable Village", "#fff", "Stone Age"),
                                                     ("check", "3. The Bronze Dawn", "#fff", "Late Prehistory"), ("flame", "6. The Market Town", GOLD, "Middle Ages")))]
    road = section("The road ahead", road_rows, style="margin-top:2px")
    body = banner + tasks + road
    w = window_w()
    left = SL + (safe_w(False) - w) / 2
    # GameUi.BottomTabsRoom: the window ends above its bottom tabs (the list never runs under them).
    room = 40 + 3 + 5
    if P():
        # The kit's task screen: from under the resource bar down to the bottom tabs, the back arrow under them.
        bottom = sheet_bottom() + room
        tw = min(150, math.floor((w - 20 - 12) / 3))
        btabs = (f'<div class="a row" style="left:{left + 10}px;bottom:{sheet_bottom()}px;gap:6px;align-items:flex-end">'
                 + tab("Chapter", True, tw, 40) + tab("Milestones", False, tw, 40, "!") + tab("First Steps", False, tw, 40) + "</div>")
        return hud(panel_open=True) + window("Chapter Tasks", body, None, bottom=bottom, top=popup_top(True)) + btabs, ""
    bottom = SB + 8 + room
    btabs = (f'<div class="a row" style="left:{left + 16}px;bottom:{SB + 8}px;gap:6px;align-items:flex-end">'
             + tab("Chapter", True, 150, 40) + tab("Milestones", False, 150, 40, "!") + tab("First Steps", False, 150, 40) + "</div>")
    return hud(panel_open=True) + window("Chapter Tasks", body, None, bottom=bottom) + btabs, ""


def page_weather():
    chip = capsule(icon("thermometer", 24) + t("−21°", NUM, extra="margin-left:4px"), 34, gap=0)
    tabs = f'<div class="grow">{t("Winter · Year 1 · next season in 1:11:55", BODY if not P() else 12.5, PALE)}</div>' + chip
    cells = ""
    for u, temp, kind, ic, col in (("Now", "−21°", "rare", "winter", ICY), ("12m", "−23°", "rare", "winter", ICY), ("42m", "−21°", "rare", "winter", ICY),
                                   ("1h12", "−3°", "rare", "thermometer", ICY), ("1h42", "2°", "common", "thermometer", "#E8F2FA"),
                                   ("2h12", "5°", "legendary", "thermometer", "#FFD27A"), ("2h42", "10°", "legendary", "thermometer", "#FFD27A"),
                                   ("3h12", "11°", "legendary", "thermometer", "#FFD27A")):
        cells += f'<div class="col grow" style="align-items:center">{t(u, SMALL, ICY if ic == "winter" else PALE)}<div style="margin-top:3px">{slot(ic, 38, kind, share=.62)}</div>{t(temp, BODY, col, "margin-top:2px")}</div>'
    strip = f'<div class="row grow" style="align-items:flex-start">{cells}</div>'
    hero = (f'<div class="row" style="margin:8px 0 6px">{slot("thermometer", 64, "rare", share=.7)}'
            f'<div class="col" style="margin:0 12px 0 10px">{t("Outside now", SMALL, PALE)}{t("−21°", 30, BAD)}{t("Feels freezing", SMALL, PALE)}</div>'
            f'{"" if P() else strip}</div>' + (f'<div class="row" style="margin-bottom:6px">{strip}</div>' if P() else ""))
    snap = line(rowicon("winter") + t("Great Frost", BODY, ICY) + t("now · ends in 1 h 11 min", BODY, PALE, "margin-left:10px", "flex") + t("−16°", NUM, ICY, "margin-left:8px"))
    forecast = section("Forecast · next 4 hours", [hero, notice("winter", "Great Frost: 16° colder than usual. Keep the woodpile full and the fires burning.", style="margin-bottom:6px"), snap],
                       style="margin-bottom:8px")
    grid = "".join(f'<div class="col grow" style="align-items:center">{reward("people", k, n, 52)}{t(lab, BODY, c, "margin-top:3px")}</div>'
                   for k, n, lab, c in (("legendary", "29", "Comfortable", "#FFD27A"), ("common", "17", "Chilly", "#E8F2FA"), ("rare", "0", "Cold", ICY), ("epic", "0", "Freezing", BAD)))
    homes = section("Homes", [f'<div class="row" style="margin:8px 0 6px">{grid}</div>',
                              line(rowicon("home") + t("Timber House · 4", BODY, cls="flex") + t("8° Chilly", NUM, "#E8F2FA", "margin-left:8px"))])
    return hud(panel_open=True) + window("Weather & Warmth", forecast + homes, tabs), ""


LAWNAME = [""]


def name_id(_ic):
    """The law id of the node being drawn (its name in lower case with underscores)."""
    return re.sub(r"[^a-z]+", "_", LAWNAME[0].lower()).strip("_")


def page_customs():
    chip = capsule(icon("clock", 22) + t("A law can be signed", BODY, extra="margin-left:5px"), 34, gap=0)
    tabs = f'<div class="grow">{t("1/14 customs signed · discontent 21", BODY, PALE)}</div>' + chip
    if P():
        tabs = f'<div class="col" style="align-items:flex-start;flex:1 1 0">{t("1/14 customs signed · discontent 21", BODY, PALE)}<div style="margin-top:4px">{chip}</div></div>'
    iw = inner_w()
    detail_w = iw if P() else 248
    col_w = math.floor((iw - detail_w - 8 - 8) / 2)

    def lawslot(ic, kind, size):
        """GameUi.LawSlot: the custom's own picture on its state's cell, a tick (signed) or padlock (locked) on the corner."""
        law = "law_" + name_id(ic)
        mark = {"check": "check", "lock": "lock"}.get(ic)
        cell = slot(law, size, kind, dim=ic == "lock", share=.72)
        if mark:
            cell = cell.replace('</div></div>', f'<div class="a" style="right:-5px;bottom:-4px">{icon(mark, round(size * .44))}</div></div></div>', 1)
        return cell

    def node(name, meta, mcol, kind, ic, sel=False, w=col_w):
        nw = w - 12 - 42 - 8
        size = max(SMALL, min(NUM, (nw - 2 * math.ceil(ow(NUM)) - 1) / max(.01, text_w(name, 1, False)))) if text_w(name, NUM) > nw else NUM
        size = math.floor(size * 4) / 4
        LAWNAME[0] = name
        return box("slot" if sel else "section", f'<div class="row" style="padding:6px 6px {9 if sel else 6}px">{lawslot(ic, kind, 42)}'
                   f'<div class="col grow" style="margin-left:8px">{t(name, size, PALE if ic == "lock" else "#fff", cls="w")}{t(meta, SMALL, mcol, cls="w")}</div></div>',
                   "min-height:60px;margin-bottom:8px")

    orbox = (f'<div class="col" style="border:2px solid #CB8A55;border-radius:12px;padding:4px;margin-bottom:8px;background:rgba(203,138,85,.18)">'
             f'{node("Shared Stew", "Can be signed", GOLD, "legendary", "customs", w=col_w - 12)}'
             f'<div class="row c" style="height:28px">{capsule(t("or", SMALL, GOLD), 24, pad="0 10px")}</div>'
             f'{node("Hearty Meals", "Can be signed", GOLD, "legendary", "customs", w=col_w - 12)}</div>').replace("margin-bottom:8px\"><div class=\"a sk", "margin-bottom:0\"><div class=\"a sk")
    c1 = (f'<div class="col" style="width:{col_w}px;flex:none"><div class="row c" style="height:28px">{icon("flame", 22)}{t("Survival 0/8", NUM, extra="margin-left:5px")}</div>'
          f'{orbox}{node("Long Days", "Can be signed", GOLD, "legendary", "customs", True)}{node("Dawn to Dusk", "Sign Long Days first", PALE, "common", "lock")}</div>')
    c2 = (f'<div class="col" style="width:{col_w}px;flex:none;margin-left:8px"><div class="row c" style="height:28px">{icon("people", 22)}{t("Community 1/6", NUM, extra="margin-left:5px")}</div>'
          f'{node("Day of Rest", "Signed", UP, "uncommon", "check")}{node("Burial Rites", "Can be signed", GOLD, "legendary", "customs")}{node("Harvest Festival", "Can be signed", GOLD, "legendary", "customs")}</div>')
    effects = "".join(f'<div class="row" style="align-items:flex-start;margin-top:5px">{icon("check" if g else "alert", 22)}{t(tx, BODY, UP if g else BAD, "margin-left:6px", "flex")}</div>'
                      for tx, g in (("+15% work", True), ("+25% illness", False), ("+2 discontent per hour", False), ("+4 discontent when signed", False)))
    LAWNAME[0] = "Long Days"
    detail = inset(f'<div class="row">{lawslot("customs", "legendary", 52)}<div class="col grow" style="margin-left:8px">{t("Survival", SMALL, PALE)}{t("Long Days", H2)}</div></div>'
                   f'{ink("Work from first light until dark. Everyone works 15% more, but falls ill more and grumbles.", BODY, extra="margin-top:6px")}'
                   f'{effects}' + btn("green", "Sign the custom", detail_w - 20, icon_name="customs").replace("flex:none", "flex:none;margin-top:8px"),
                   f"width:{detail_w}px", "10px 10px")
    if P():
        # GameUi.BuildCustomsPortrait: the chosen custom on top, then each branch full width.
        cw = iw
        orbox_p = (f'<div class="col" style="border:2px solid #CB8A55;border-radius:12px;padding:4px;margin-bottom:8px;background:rgba(203,138,85,.18)">'
                   f'{node("Shared Stew", "Can be signed", GOLD, "legendary", "customs", w=cw - 12)}'
                   f'<div class="row c" style="height:28px">{capsule(t("or", SMALL, GOLD), 24, pad="0 10px")}</div>'
                   f'{node("Hearty Meals", "Can be signed", GOLD, "legendary", "customs", w=cw - 12)}</div>')
        body = (f'<div class="col"><div style="margin-bottom:8px">{detail}</div>'
                f'<div class="row c" style="height:30px;flex:none">{icon("flame", 22)}{t("Survival 0/8", NUM, extra="margin-left:5px")}</div>'
                f'{orbox_p}{node("Long Days", "Can be signed", GOLD, "legendary", "customs", True, cw)}{node("Dawn to Dusk", "Sign Long Days first", PALE, "common", "lock", w=cw)}</div>')
        return hud(panel_open=True) + window("Book of Customs", body, tabs), ""
    body = f'<div class="row" style="align-items:flex-start">{c1}{c2}<div style="margin-left:8px">{detail}</div></div>'
    return hud(panel_open=True) + window("Book of Customs", body, tabs), ""


# MARK: - Dialogs

def dialog(title, body, foot, w=680, narrow=False):
    """GameUi.DialogShell: centred under the top strip, as tall as its content (portrait: a bottom sheet across the screen)."""
    sw = safe_w(narrow)
    w = min(w, sw - 40)
    sl = 0 if narrow else SL
    side, just, top = 16, "center", POPUP_TOP
    if P():
        w, side, just, top = sheet_w(), 8, "flex-end", popup_top(True)
    return (f'<div class="a col" style="align-items:center;justify-content:{just};left:{sl + side}px;right:{(0 if narrow else SR) + side}px;top:{top + 4}px;bottom:{bottom_pad() + 8}px">'
            + box("panel", f'<div class="col" style="max-height:{H - top - bottom_pad() - 10}px">'
                  f'<div style="margin:-6px -16px 0">{plank(title)}</div>'
                  f'<div class="col nos" style="padding:10px 0 4px;overflow:hidden">{body}</div>'
                  f'<div class="row c" style="gap:16px;padding-top:8px">{foot}</div></div>', f"width:{w}px", pad="0 12px 12px") + "</div>")


SCRIM = '<div class="a" style="inset:0;background:rgba(26,15,8,.45)"></div>'


def page_event():
    left = (f'<div class="col" style="{"" if P() else "width:230px;"}flex:none"><div class="row c dark" style="height:{76 if P() else 90}px;border-radius:10px;border:2.5px solid #3A2314;'
            f'background:linear-gradient(180deg,#8A5A32,#4A2E1A)">{icon("campfire", 68)}</div>'
            f'{ink("A small band from downriver has arrived with flint and dried fish. They want food and timber in exchange.", BODY, extra="margin-top:8px")}'
            f'<div class="row" style="align-items:flex-start;margin-top:8px">{icon("legacy", 22)}{ink("Your choice is written into the Legacy Timeline.", SMALL, "#8A6038", "margin-left:6px")}</div></div>')
    opts = ""
    for i, (label, sub, c) in enumerate((("Trade food for flint", "Give 20 food, receive 30 stone.", [("food", "20", False)]),
                                          ("Share a meal and stories", "Give 25 wood for their fire, learn their ways: +25 knowledge.", [("wood", "25", False)]),
                                          ("Send them on their way", "No cost, no gain.", []))):
        on = i == 0
        cs = costline(c) if c else ""
        opts += box("slot" if on else "section", f'<div class="col" style="padding:10px 12px {13 if on else 10}px">'
                    f'<div class="row">{icon("check" if on else "next", 26 if on else 22)}{t(label, NUM, extra="margin-left:8px", cls="flex")}{cs}</div>'
                    f'{t(sub, SMALL, PALE, "margin:3px 0 0 34px", "w")}</div>', "min-height:52px;margin-bottom:8px;flex:none")
    if P():
        # GameUi.TwoColumns: stacked; the buttons share the sheet's width.
        body = f'<div class="col">{left}<div class="col" style="margin-top:10px">{opts}</div></div>'
        bw = math.floor((sheet_w() - 24 - 12) / 2)
        foot = btn("blue", "Decide later", bw, size=17) + btn("green", "Confirm", bw, size=17)
        return hud(panel_open=True) + SCRIM + dialog("Wandering Traders", body, foot).replace("gap:16px", "gap:12px"), ""
    body = f'<div class="row" style="align-items:flex-start">{left}<div class="col grow" style="margin-left:12px">{opts}</div></div>'
    foot = btn("blue", "Decide later", 220, size=17) + btn("green", "Confirm", 220, size=17)
    return hud(panel_open=True) + SCRIM + dialog("Wandering Traders", body, foot), ""


def page_welcome():
    cells = "".join(f'<div style="margin-right:10px">{reward(i, k, a, 58)}</div>' for i, k, a in (("food", "uncommon", "+320"), ("wood", "legendary", "+1.2K"), ("stone", "common", "+410"),
                                                                                                ("ore", "rare", "+38"), ("hides", "legendary", "+12"), ("knowledge", "epic", "+96")))
    body = (t("Your people kept the fire alive for 2 h 14 min.", NUM, cls="w") + ink("Time away counts for up to 8 hours.", SMALL, extra="margin-top:3px")
            + section("Gathered", [f'<div class="row" style="margin:10px 0 8px;flex-wrap:wrap;row-gap:8px">{cells}</div>'], style="margin-top:8px")
            + section("The village", [line(rowicon("check") + t("Two newcomers settled in", BODY, cls="flex"), False, ROW_TIGHT),
                                      line(rowicon("check") + t("The Timber House was finished", BODY, cls="flex"), True, ROW_TIGHT)], style="margin-top:8px"))
    foot = btn("green", "Back to the village", 280, size=17)
    return hud(panel_open=True) + SCRIM + dialog("Welcome back, Founder", body, foot, 600), ""


def page_story():
    scrim = '<div class="a" style="inset:0;background:linear-gradient(180deg,rgba(20,12,6,.25),rgba(20,12,6,.6))"></div>'
    if P():
        return page_story_p(scrim)
    left, right = SL + 12, SR + 12
    portrait, overlap = 210, 100
    text_left = portrait - overlap + 14
    paras = ("The winter in the north was the hardest anyone could remember. When the reindeer did not return, the families followed the river south, carrying fire in a clay pot.",
             "After many days they came to a valley none of them had seen: a quiet river, a great lake beyond the trees and hills of grey stone.")
    text = "".join(t(p, NUM, extra="margin-bottom:8px", cls="w") for p in paras)
    goal = inset(f'{icon("goals", 26)}{t("Survive the first year and make the camp a stable village.", BODY, GOLD, "margin-left:8px", "flex")}', "margin-top:2px", cls="row")
    boxh = (f'<div class="a" style="left:{left + overlap}px;right:{right}px;bottom:{SB + 6}px;max-height:{H - POPUP_TOP - SB - 6}px">'
            + box("panel", f'<div class="col">{t("Chapter 1 · Stone Age", BODY, PALE)}{t("The Riverside Camp", H1 + 4, GOLD, "margin-top:2px")}'
                  f'<div class="col" style="padding-top:6px;overflow:hidden">{text}{goal}</div>'
                  f'<div class="row" style="justify-content:flex-end;padding-top:8px">{btn("green", "Begin", 180, size=17)}{icon("next", 26, "transform:rotate(90deg);margin-left:12px")}</div></div>',
                  "width:100%", pad=f"14px 22px 14px {text_left}px") + "</div>")
    face = f'<div class="a" style="left:{left}px;bottom:{SB + 6}px">{icon("face_f1", portrait)}</div>'
    return hud(panel_open=True) + scrim + boxh + face, ""


def page_story_p(scrim):
    """GameUi.StoryBoxPortrait: the narrator standing on the box's top-left edge, the title beside their feet."""
    portrait, overlap = 170, 34
    w = sheet_w()
    paras = ("The winter in the north was the hardest anyone could remember. When the reindeer did not return, the families followed the river south, carrying fire in a clay pot.",
             "After many days they came to a valley none of them had seen: a quiet river, a great lake beyond the trees and hills of grey stone.")
    text = "".join(t(p, NUM, extra="margin-bottom:8px", cls="w") for p in paras)
    goal = inset(f'{icon("goals", 26)}{t("Survive the first year and make the camp a stable village.", BODY, GOLD, "margin-left:8px", "flex")}', "margin-top:2px", cls="row")
    head_w = w - 32 - (portrait - 30)
    title_size = max(BODY, min(H1 + 2, (head_w - 2 * math.ceil(ow(H1 + 2)) - 1) / text_w("The Riverside Camp", 1, False)))
    face = f'<div class="a" style="left:-6px;top:{overlap - portrait}px">{icon("face_f1", portrait)}</div>'
    boxh = (f'<div class="a" style="left:{SL + (safe_w() - w) / 2}px;width:{w}px;bottom:{bottom_pad() + 6}px">'
            + box("panel", f'<div class="col"><div class="col" style="margin-left:{portrait - 30}px">{t("Chapter 1 · Stone Age", BODY, PALE)}'
                  f'{t("The Riverside Camp", math.floor(title_size * 4) / 4, GOLD, "margin-top:2px")}</div>'
                  f'<div class="col" style="padding-top:8px;overflow:hidden">{text}{goal}</div>'
                  f'<div class="row" style="justify-content:flex-end;padding-top:8px">{btn("green", "Begin", 180, size=17)}{icon("next", 26, "transform:rotate(90deg);margin-left:12px")}</div></div>{face}',
                  "width:100%", pad="14px 16px 14px 16px") + "</div>")
    return hud(panel_open=True) + scrim + boxh, ""


def page_loading():
    """LoadingScreen: the montage (bg here) filling the screen, the title, the tip, the bar and the version."""
    top = f"top:{ST + 64}px" if P() else "top:9%"
    side = 16 if P() else 24
    b = bottom_pad()
    shades = ('<div class="a" style="left:0;right:0;top:0;height:45%;background:linear-gradient(180deg,rgba(20,13,5,.55),rgba(20,13,5,0))"></div>'
              '<div class="a" style="left:0;right:0;bottom:0;height:45%;background:linear-gradient(0deg,rgba(20,13,5,.6),rgba(20,13,5,0))"></div>')
    brand = (f'<div class="a col" style="left:0;right:0;{top};align-items:center">{icon("logo_mark", 70).replace("Icons/logo_mark", "Images/logo_mark")}'
             f'{t("FOUNDERS", 38, extra="letter-spacing:4px;margin-top:6px")}{t("THE LONG RISE", 14, GOLD, "letter-spacing:3px")}'
             f'{t("Build a world that outlives you.", BODY, PALE, "margin-top:6px")}</div>')
    tip = box("panel", f'<div class="row" style="padding:7px 14px 11px 12px">{icon("knowledge", 32)}<div class="col grow" style="margin-left:10px">'
              f'{t("Founder&#39;s tip", SMALL, GOLD)}{t("Food spoils in the open. A storage pit keeps part of it safe.", BODY, cls="w")}</div></div>', "width:100%;max-width:600px")
    tip_row = f'<div class="a col" style="left:0;right:0;bottom:{b + 76}px;align-items:center;padding:0 {side}px">{tip}</div>'
    prog = (f'<div class="a col" style="left:0;right:0;bottom:{b + 34}px;align-items:center;padding:0 {side}px"><div class="col" style="width:100%;max-width:520px">'
            f'<div class="row" style="justify-content:space-between">{t("Kindling the hearth…", BODY, PALE)}{t("64%", NUM, GOLD)}</div>{bar(.64, "100%", 12, extra="margin-top:4px")}</div></div>')
    ver = f'<div class="a row c" style="left:0;right:0;bottom:{b + 10}px">{t("v0.1.0 · Era I — First Communities", SMALL, PALE)}</div>'
    return '<div class="a" style="inset:0;background:rgba(13,15,20,.38)"></div>' + shades + brand + tip_row + prog + ver, ""


def page_settings():
    seg = "".join(tab(n, i == 2) for i, n in enumerate(("Smooth", "Balanced", "Cinematic")))
    gfx = section("Graphics", [f'<div class="row" style="gap:6px;margin:8px 0;align-items:flex-end">{seg}</div>'], style="margin-bottom:8px")
    iw = inner_w()

    def toggle(label, hint, on, sep):
        sw = box("btn_green" if on else "btn_grey", f'<div class="row" style="height:100%;padding:0 5px 4px 7px">'
                 + (f'<div class="row c grow">{t("ON", BODY)}</div>{box("slot", "", "width:24px;height:24px;flex:none")}' if on
                    else f'{box("slot", "", "width:24px;height:24px;flex:none")}<div class="row c grow">{t("OFF", BODY)}</div>') + '</div>', "width:80px;height:36px;flex:none")
        return line(f'<div class="col grow">{t(label, NUM)}{t(hint, SMALL, PALE)}</div>{sw}', sep, 56)
    game = section("Game", [toggle("Battery saver", "30 fps, lighter on the battery", False, False), toggle("Haptics", "A tap of vibration on milestones", True, True),
                            toggle("Music", "Calm themes between the seasons", True, True)], style="margin-bottom:8px")
    two = P() and iw < 480
    dev_w = (iw - 20 - 8) / 2 if two else (iw - 20 - 24) / 4
    btns = [btn("blue", n, None, 42, size=NUM, share=True) for n in ("Save now", "+50 all", "+1 min", "+10 min")]
    rows = ([f'<div class="row" style="gap:8px;margin-top:8px">{btns[0]}{btns[1]}</div>', f'<div class="row" style="gap:8px;margin-top:8px">{btns[2]}{btns[3]}</div>'] if two
            else [f'<div class="row" style="gap:8px;margin-top:8px">{"".join(btns)}</div>'])
    rows.append(f'<div style="margin:8px 0">{btn("red", "Reset settlement", iw - 20, 42, size=NUM, icon_name="trash")}</div>')
    dev = section("Developer", rows)
    return hud(panel_open=True) + window("Settings", gfx + game + dev), ""


PAGES = {
    "01_hud": page_hud, "02_heart_card": page_heart, "03_workplace_card": page_workplace,
    "04_build": page_build, "05_people": page_people, "05b_people_work": page_people_work, "06_research": page_research,
    "07_goals": page_goals, "08_event": page_event, "09_story": page_story, "10_welcome_back": page_welcome,
    "11_weather": page_weather, "12_customs": page_customs, "13_settings": page_settings, "14_loading": page_loading,
}

# Finds text that does not fit: wider than its own box (clipped), or reaching out of the nearest skinned box.
CHECK_JS = r"""
<script>
addEventListener('load', () => setTimeout(() => {
  const out = [];
  for (const e of document.querySelectorAll('.o, .ink')) {
    const r = e.getBoundingClientRect();
    if (r.width === 0) continue;
    const txt = e.textContent.trim().slice(0, 40);
    if (e.scrollWidth > e.clientWidth + 1) out.push('clipped: ' + txt);
    const bx = e.parentElement.closest('.bx');
    if (!bx) continue;
    const b = bx.getBoundingClientRect();
    const slack = bx.querySelector(':scope > .a.sk-tile, :scope > [class*="sk-cell_"]') ? 9 : 1;
    if (r.left < b.left - slack || r.right > b.right + slack) out.push('spills: ' + txt + ' (' + Math.round(r.left) + '..' + Math.round(r.right) + ' in ' + Math.round(b.left) + '..' + Math.round(b.right) + ')');
  }
  const pre = document.createElement('pre'); pre.id = 'fit-report'; pre.textContent = out.join('\n'); pre.style.display = 'none';
  document.body.appendChild(pre);
}, 50));
</script>"""


def main():
    out = sys.argv[1] if len(sys.argv) > 1 else os.path.join(HERE, "png")
    bg = sys.argv[2] if len(sys.argv) > 2 else None
    os.makedirs(out, exist_ok=True)
    if bg:
        Image.open(bg).convert("RGB").resize((1688, 780), Image.LANCZOS).save(os.path.join(HERE, "bg.jpg"), quality=86)
    with open(os.path.join(HERE, "kit.css"), "w") as f:
        f.write(css())
    problems = 0
    only = os.environ.get("MOCKS")  # e.g. MOCKS=p_04 renders the pages whose names start with it
    for o in ORIENTATIONS:
        set_orient(o)
        for base, fn in PAGES.items():
            name = f"{o}_{base}"
            if only and not any(name.startswith(x) for x in only.split(",")):
                continue
            body, body_style = fn()
            width = W
            doc = (f'<!doctype html><html><head><meta charset="utf-8"><link rel="stylesheet" href="kit.css"></head>'
                   f'<body style="width:{width}px;height:{H}px;{body_style}">{body}{CHECK_JS}</body></html>')
            path = os.path.join(HERE, name + ".html")
            with open(path, "w") as f:
                f.write(doc)
            render_page(path, os.path.join(out, name + ".png"), width)
            report = fit_report(path, width)
            print("mock:", name, "ok" if not report else "")
            for r in report:
                print("   ", r)
            problems += len(report)
    set_orient("l")
    if problems:
        print(f"{problems} text fit problem(s)")
        sys.exit(1)


def chrome_args(width):
    return [kit.chromium(), "--headless", "--no-sandbox", "--disable-gpu", "--hide-scrollbars", "--allow-file-access-from-files",
            f"--window-size={width},{H}", "--virtual-time-budget=4000"]


def render_page(path, png, width):
    with tempfile.TemporaryDirectory() as tmp:
        shot = os.path.join(tmp, "s.png")
        subprocess.run(chrome_args(width) + ["--force-device-scale-factor=2", f"--screenshot={shot}", "file://" + path],
                       check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        Image.open(shot).convert("RGB").crop((0, 0, width * 2, H * 2)).save(png)


def fit_report(path, width):
    dom = subprocess.run(chrome_args(width) + ["--dump-dom", "file://" + path], check=True, capture_output=True, text=True).stdout
    m = re.search(r'<pre id="fit-report"[^>]*>(.*?)</pre>', dom, re.S)
    if not m:
        return ["(no fit report)"]
    return [htmlmod.unescape(x) for x in m.group(1).split("\n") if x.strip()]


if __name__ == "__main__":
    main()
