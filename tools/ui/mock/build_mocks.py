#!/usr/bin/env python3
"""HTML mocks of the main screens at 844 x 390 points (iPhone landscape with the notch insets), built
from the same skins, icons, font and measurements as the Unity UI (GameUi*.cs / UiKit.cs), and
rendered with headless Chromium so the design can be reviewed without Unity.

Usage: python3 tools/ui/mock/build_mocks.py [out_dir] [background.png]
Writes tools/ui/mock/*.html (+ kit.css, bg.jpg) and PNGs (2x) into out_dir.
"""
import os
import sys

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))
import build_ui_assets as kit  # noqa: E402

UI = "../../../unity/Assets/Art/UI/Resources/UI/"
W, H = 844, 390
# Insets of a notched iPhone in landscape (points): the notch side and the home indicator.
SL, SR, SB, ST = 44, 44, 21, 0

# MARK: - Metrics (mirrors GameUi / UiKit constants)
TOP_Y = ST + 6          # top strip
TOP_H = 40
CHIPS_Y = TOP_Y + 31    # second line under the capsule
RAIL_Y = ST + 74        # rails start
TILE = 46               # right column menu tiles
TILE_GAP = 6
BUILD_BTN = 58
CARD_W = 330            # docked building card
WIN_W = 600             # centred window max width


def ow(size):
    """Outline width for outlined text (UiKit.Theme.OutlineFor)."""
    return max(1.0, min(2.2, size * 0.12))


def css():
    rules = ["@font-face{font-family:Changa;src:url(" + UI + "Fonts/ChangaOne-Regular.ttf)}",
             "*{box-sizing:border-box}",
             "body{margin:0;width:%dpx;height:%dpx;position:relative;overflow:hidden;font-family:Changa;background:#333 url(bg.jpg) center/cover}" % (W, H),
             ".a{position:absolute}.row{display:flex;flex-direction:row;align-items:center}.col{display:flex;flex-direction:column}",
             ".grow{flex:1 1 0;min-width:0}.c{justify-content:center;align-items:center}.nw{white-space:nowrap}",
             # Outlined text: white fill, dark outline outside the glyph (paint-order) and a hard drop.
             ".o{color:#fff;paint-order:stroke fill;-webkit-text-stroke-color:#3A2314;line-height:1.06;overflow:hidden;white-space:nowrap}",
             ".ink{color:#5A3A1E;line-height:1.12}",
             ".ic{background-size:100% 100%;flex:none}",
             ".nos>*{flex-shrink:0}.eq>div[style^='position:relative;height']{flex:1 1 0 !important}"]
    for name, (_h, w, hgt, l, t, r, b) in kit.SKINS.items():
        s = kit.SKIN_SCALE
        rules.append(f".sk-{name}{{border-style:solid;border-width:{t}px {r}px {b}px {l}px;"
                     f"border-image:url({UI}Skin/{name}.png) {t * s} {r * s} {b * s} {l * s} fill / {t}px {r}px {b}px {l}px stretch}}")
    return "\n".join(rules)


# MARK: - Components (same names as the C# helpers)

def t(text, size, color="#fff", extra=""):
    w = ow(size)
    import math
    return (f'<div class="o" style="font-size:{size}px;-webkit-text-stroke-width:{2 * w:.2f}px;padding:0 {math.ceil(w)}px;'
            f'text-shadow:0 {w + 1:.2f}px 0 #3A2314;color:{color};{extra}">{text}</div>')


def ink(text, size=11, color="#5A3A1E", extra=""):
    return f'<div class="ink" style="font-size:{size}px;color:{color};{extra}">{text}</div>'


def icon(name, size, extra=""):
    return f'<div class="ic" style="width:{size}px;height:{size}px;background-image:url({UI}Icons/{name}.png);{extra}"></div>'


def skin(name, inner="", style="", cls=""):
    """An element with a 9-slice skin; its content box sits inside the slices, so pad negatively like UITK."""
    return f'<div class="sk-{name} {cls}" style="{style}">{inner}</div>'


def box(name, inner, style="", cls="", pad="0"):
    # UITK draws the 9-slice over the padding box; here the border-image does the same and the content
    # gets the padding we choose (margins absorb the slice thickness).
    _h, _w, _hh, l, tt, r, b = kit.SKINS[name]
    return (f'<div class="{cls}" style="position:relative;{style}">'
            f'<div class="a sk-{name}" style="inset:0"></div>'
            f'<div style="position:relative;padding:{pad};height:100%;{"" if "col" in cls or "row" in cls else ""}" class="{cls}">{inner}</div></div>')


def btn(kind, label, w=None, h=34, sub=None, size=13, icon_name=None):
    sw = f"width:{w}px;" if w else ""
    inner = f'<div class="row c" style="gap:4px">{icon(icon_name, size + 4) if icon_name else ""}{t(label, size)}</div>'
    if sub:
        inner += f'<div class="row c" style="gap:3px;margin-top:-1px">{sub}</div>'
    return box(f"btn_{kind}", f'<div class="col c" style="height:100%;padding-bottom:3px">{inner}</div>', f"{sw}height:{h}px;flex:none")


def sq(kind, icon_name, size=30):
    return box(f"btn_{kind}", f'<div class="col c" style="height:100%;padding-bottom:3px">{icon(icon_name, size * 0.56)}</div>', f"width:{size}px;height:{size}px;flex:none")


def cost(icon_name, have, need=None, short=False, size=10.5):
    txt = f'<span style="color:{"#FF6A5A" if short else "#fff"}">{have}</span>' + (f" / {need}" if need else "")
    return f'<div class="row" style="gap:2px">{icon(icon_name, size + 4)}{t(txt, size)}</div>'


def bar(frac, w="100%", h=10, kind="green", label=None):
    fill = f'<div class="a sk-fill_{kind}" style="left:0;top:0;bottom:0;width:{max(frac * 100, 0)}%;min-width:{h}px"></div>' if frac > 0 else ""
    lab = f'<div class="a row c" style="inset:0">{t(label, h - 1)}</div>' if label else ""
    return f'<div style="position:relative;width:{w};height:{h}px;flex:none"><div class="a sk-bar_track" style="inset:0"></div>{fill}{lab}</div>'


def badge(text, kind="green", size=15):
    return box(f"badge_{kind}", f'<div class="row c" style="height:100%;padding:0 3px 1px">{t(text, size * 0.62)}</div>', f"min-width:{size}px;height:{size}px;flex:none")


def plank(title, lv=None, size=15, w="100%", extra=""):
    lvh = f' <span style="color:#D7F25A">Lv.{lv}</span>' if lv is not None else ""
    return box("plank", f'<div class="row c" style="height:100%;padding:0 30px 3px">{t(title + lvh, size)}</div>{extra}', f"width:{w};height:30px;flex:none")


def section(title, rows, extra_head="", style=""):
    head = (f'<div class="row" style="height:20px;padding:0 8px;background:#D9A266;border-radius:7px 7px 0 0;border-bottom:1px solid #BF884E;gap:5px">'
            f'{t(title, 11.5)}<div class="grow"></div>{extra_head}</div>')
    body = "".join(rows)
    return (f'<div style="position:relative;{style}"><div class="a sk-section" style="inset:0"></div>'
            f'<div class="col" style="position:relative">{head}<div class="col" style="padding:0 8px 3px">{body}</div></div></div>')


def statrow(icon_name, label, now, nxt=None, sep=True, size=11.5):
    right = t(now, 12)
    if nxt is not None:
        right += icon("arrow", 13, "margin:0 4px") + t(nxt, 12, "#B6F25A")
    return (f'<div class="row" style="height:23px;gap:5px;{"border-top:1px solid #CFA878;" if sep else ""}">'
            f'{icon(icon_name, 15) if icon_name else ""}{t(label, size)}<div class="grow"></div>{right}</div>')


def reqrow(content, met, sep=True, go="Go"):
    right = icon("check", 20) if met else btn("blue", go, 44, 24, size=11.5)
    return f'<div class="row" style="height:27px;gap:5px;{"border-top:1px solid #CFA878;" if sep else ""}">{content}<div class="grow"></div>{right}</div>'


def slot(icon_name, size=44, kind="slot", badge_text=None, lock=False, label=None, dim=False, share=None):
    b = f'<div class="a" style="left:-3px;bottom:-1px">{badge(badge_text, "green", 15)}</div>' if badge_text else ""
    lk = f'<div class="a" style="right:-3px;bottom:-1px">{icon("lock", 16)}</div>' if lock else ""
    lab = f'<div class="a row c" style="left:0;right:0;bottom:4px">{t(label, 9.5)}</div>' if label else ""
    op = "opacity:.55;" if dim else ""
    return box(kind if kind == "slot" else ("tile" if kind == "tile" else f"cell_{kind}"),
               f'<div class="col c" style="height:100%;padding-bottom:3px;{op}">{icon(icon_name, size * (share or (0.86 if icon_name and icon_name.startswith("face") else 0.66))) if icon_name else ""}</div>{b}{lk}{lab}',
               f"width:{size}px;height:{size}px;flex:none")


def tab(label, on, w=None, h=26, count=None, size=12):
    h = h + 2 if on else h
    sw = f"width:{w}px;" if w else "flex:1 1 0;"
    lab = label + (f' <span style="color:#D7F25A">{count}</span>' if count else "")
    return box("tab_on" if on else "tab_off", f'<div class="row c" style="height:100%;padding-bottom:2px">{t(lab, size, "#fff" if on else "#F3DCC0")}</div>', f"{sw}height:{h}px")


def capsule(inner, h=28, style=""):
    return box("capsule", f'<div class="row" style="height:100%;padding:0 9px;gap:8px">{inner}</div>', f"height:{h}px;{style}")


def window(title, body, tabs=None, lv=None, foot=None, w=620, top=ST + 50, bottom=None, back=True):
    bottom = SB + 8 if bottom is None else bottom
    left = (W - w) / 2
    tabs_html = f'<div class="row" style="gap:4px;padding:4px 10px 0;align-items:flex-end">{tabs}</div>' if tabs else ""
    foot_html = f'<div class="row c" style="gap:10px;padding:4px 12px 10px">{foot}</div>' if foot else ""
    win = (f'<div class="a col" style="left:{left}px;top:{top}px;width:{w}px;bottom:{bottom}px">'
           f'<div class="a sk-panel" style="inset:0"></div>'
           f'<div class="col" style="position:relative;height:100%">'
           f'<div style="margin:-6px -6px 0">{plank(title, lv, 16)}</div>'
           f'<div class="a" style="right:-8px;top:-9px">{sq("red", "close", 30)}</div>'
           f'{tabs_html}<div class="col grow" style="min-height:0;overflow:hidden;padding:6px 10px 6px">{body}</div>{foot_html}</div></div>')
    if back:
        win += f'<div class="a" style="left:{SL + 6}px;bottom:{SB + 4}px">{icon("back", 46)}</div>'
    return win


def dialog(title, body, foot, w=600):
    """GameUi.DialogShell: centred under the top strip, as tall as its content."""
    top = ST + 54
    return (f'<div class="a col c" style="left:{SL}px;right:{SR}px;top:{top}px;bottom:{SB + 8}px">'
            f'<div class="col" style="position:relative;width:{w}px;max-height:100%">'
            f'<div class="a sk-panel" style="inset:0"></div><div class="col" style="position:relative;padding:0 10px 10px">'
            f'<div style="margin:-4px -14px 0">{plank(title, None, 16)}</div>'
            f'<div class="col nos" style="padding:8px 0 4px">{body}</div>'
            f'<div class="row c eq" style="gap:6px;padding-top:6px">{foot}</div></div></div></div>')


# MARK: - HUD

def hud(narrow=False, panel_open=False, card_open=False):
    sl = 0 if narrow else SL
    sr = 0 if narrow else SR
    width = 693 if narrow else W
    res = [("food", "656"), ("wood", "1.3K"), ("stone", "958"), ("ore", "120"), ("hides", "84"), ("knowledge", "231")]
    items = (f'<div class="row" style="gap:3px">{icon("summer", 18)}{t("12:00", 13)}</div>'
             f'<div class="row" style="gap:3px">{icon("people", 19)}{t("43/105", 13)}{badge("!", "red", 14)}</div>'
             + "".join(f'<div class="row" style="gap:3px">{icon(i, 18)}{t(v, 13)}</div>' for i, v in res))
    out = [f'<div class="a" style="left:{sl + 8}px;top:{TOP_Y}px">{slot("landmark", 40, "tile")}'
           f'<div class="a" style="left:3px;right:3px;bottom:-4px">{bar(0.62, "100%", 7)}</div>'
           f'<div class="a" style="right:-4px;top:-4px">{badge("!", "red", 14)}</div></div>',
           f'<div class="a" style="left:{sl + 54}px;top:{TOP_Y + 3}px">{capsule(items, 28)}</div>',
           f'<div class="a" style="right:{sr + 8}px;top:{TOP_Y + 2}px">{sq("brown", "settings", 30)}</div>']
    if not card_open and not panel_open:
        chips = (f'<div class="row" style="gap:3px">{icon("summer", 15)}{t("Summer Y1", 11)}</div>'
                 f'<div class="row" style="gap:2px">{icon("thermometer", 15)}{t("21°", 11)}</div>'
                 f'<div class="row" style="gap:2px">{icon("approval", 15)}{t("100", 11)}</div>'
                 f'<div class="row" style="gap:2px">{icon("discontent", 15)}{t("33", 11)}</div>'
                 f'<div class="row" style="gap:2px">{icon("sick", 15)}{t("2", 11, "#FFB3A8")}</div>')
        out.append(f'<div class="a" style="left:{sl + 54}px;top:{CHIPS_Y + 3}px">{capsule(chips, 21, "")}</div>')
    if panel_open:
        return "".join(out)
    if not card_open:
        # Goal card and promise (left rail).
        goal = box("panel", f'<div class="col" style="padding:5px 8px 7px;gap:4px"><div class="row" style="gap:5px">{icon("goals", 18)}'
                   f'{t("Build a storage pit near the fire", 11.5, extra="white-space:normal")}</div>{bar(0.5, "100%", 8)}</div>', "width:206px")
        out.append(f'<div class="a" style="left:{sl + 8}px;top:{RAIL_Y}px">{goal}</div>')
        # Timers next to the menu column.
        timers = ""
        for ic, name, tm, fr, k in (("knowledge", "Pottery", "1:12", .4, "blue"), ("build", "Timber House", "0:27", .7, "gold")):
            timers += capsule(f'{icon(ic, 20)}<div class="col grow" style="gap:2px"><div class="row">{t(name, 10.5)}<div class="grow"></div>{t(tm, 10.5)}</div>{bar(fr, "100%", 7, k)}</div>', 34, "width:142px;margin-bottom:5px")
        out.append(f'<div class="a col" style="right:{sr + 8 + TILE + 8}px;top:{RAIL_Y}px">{timers}</div>')
    # Right column: menu tiles (labels inside) and the Build button.
    tiles = "".join(f'<div style="margin-bottom:{TILE_GAP}px;position:relative">{slot(i, TILE, "tile", label=l)}{f"<div class=a style=right:-5px;top:-5px>{badge(b, chr(114)+chr(101)+chr(100), 16)}</div>" if b else ""}</div>'
                    for i, l, b in (("goals", "Goals", "!"), ("people", "People", "29"), ("knowledge", "Research", None), ("customs", "Customs", "!")))
    out.append(f'<div class="a col" style="right:{sr + 8}px;top:{RAIL_Y}px">{tiles}</div>')
    out.append(f'<div class="a" style="right:{sr + 6}px;bottom:{(SB if not narrow else 10) + 4}px">{box("btn_orange", f"<div class=col style=height:100%;align-items:center;justify-content:center;padding-bottom:4px>{icon(chr(98)+chr(117)+chr(105)+chr(108)+chr(100), 30)}{t(chr(66)+chr(117)+chr(105)+chr(108)+chr(100), 11.5)}</div>", f"width:{BUILD_BTN}px;height:{BUILD_BTN}px")}</div>')
    if not card_open:
        bl = (f'{box("tile", f"<div class=col style=height:100%;align-items:center;justify-content:center;padding-bottom:3px>{icon(chr(99)+chr(111)+chr(109)+chr(112)+chr(97)+chr(115)+chr(115), 34)}</div>", "width:50px;height:50px")}'
              + "".join(sq("brown", i, 34) for i in ("world", "legacy", "eye_off")))
        out.append(f'<div class="a row" style="left:{sl + 8}px;bottom:{(SB if not narrow else 10) + 4}px;gap:6px;align-items:flex-end">{bl}</div>')
        news = capsule(f'{icon("bell", 16)}{t("A new member joined the community (population 43)", 11)}', 24, "max-width:330px")
        out.append(f'<div class="a row c" style="left:0;right:0;bottom:{(SB if not narrow else 10) + 8}px">{news}</div>')
    return "".join(out)


def toast():
    n = box("panel", f'<div class="row" style="padding:4px 10px 6px;gap:6px">{icon("check", 18)}{t("A new member joined the community", 11.5)}</div>')
    return f'<div class="a row c" style="left:0;right:0;top:{RAIL_Y}px">{n}</div>'


def page_hud(narrow=False):
    style = "width:693px" if narrow else ""
    return hud(narrow) + toast(), style


# MARK: - Building card (heart: mirrors the kit's Watchtower popup)

def page_heart():
    rows_stats = [statrow("flame", "Warmth", "+12°", "+13°", False), statrow("compass", "Reach", "6 cells", "7"),
                  statrow("people", "Comfort for", "105", "120")]
    rows_res = [reqrow(f'{icon("level_up", 16)}{t("Timber House", 11.5)}{t(" Lv.4", 11.5, "#D7F25A")}', True, False),
                reqrow(f'{icon("knowledge", 16)}{t("Research Pottery", 11.5)}', False),
                reqrow(cost("wood", "1.3K", "1.9K", True, 11.5), False, go="Go"),
                reqrow(cost("stone", "958", "600", False, 11.5), True)]
    reach = ('<div class="row c" style="height:58px;position:relative">'
             '<div class="a" style="left:50%;top:50%;width:150px;height:46px;margin:-23px 0 0 -75px;border-radius:50%;background:radial-gradient(closest-side,rgba(255,200,70,.55),rgba(255,160,40,.12) 80%,transparent)"></div>'
             f'<div class="a" style="left:calc(50% - 22px);top:4px">{icon("campfire", 44)}</div>'
             f'<div class="a row" style="left:calc(50% + 34px);top:30px;gap:8px">{t("6", 11.5, "#FFE07A")}{icon("arrow", 12)}{t("7 cells", 11.5, "#B6F25A")}</div></div>')
    body = (reach + t("Keep the fire burning: it warms every home in reach.", 11, extra="white-space:normal;text-align:center;margin:-2px 0 5px")
            + section("Upgrade Stats", rows_stats, style="margin-bottom:6px")
            + section("Upgrade Resources", rows_res))
    foot = (f'{sq("red", "trash", 34)}'
            f'{btn("orange", "Stoke", None, 36, cost("wood", "60", None, False, 10), 12.5)}'
            f'{btn("green", "Upgrade", None, 36, f"""{icon("clock", 13)}{t("4:00", 10)}""", 12.5)}')
    return hud(card_open=True) + card("Great Hearth", 30, body, foot), ""


def card(title, lv, body, foot, w=CARD_W):
    return (f'<div class="a col" style="left:{SL + 8}px;top:{ST + 50}px;width:{w}px;bottom:{SB + 8}px">'
            f'<div class="a sk-panel" style="inset:0"></div><div class="col" style="position:relative;height:100%">'
            f'<div style="margin:-6px -6px 0">{plank(title, lv, 15)}</div>'
            f'<div class="a" style="right:-8px;top:-9px">{sq("red", "close", 30)}</div>'
            f'<div class="col grow nos" style="min-height:0;overflow:hidden;padding:4px 9px 0">{body}</div>'
            f'<div class="row eq" style="gap:6px;padding:5px 9px 9px">{foot}</div></div></div>')


# MARK: - Workplace card (workers like the kit's Recruit tab)

def page_workplace():
    head = (f'<div class="row" style="gap:8px;margin-bottom:2px;align-items:flex-start">{slot("axe", 48, "common")}<div class="col grow" style="gap:1px">'
            f'{t("Work", 11.5)}{ink("Cuts wood from the trees around it. Workers here gather faster near a road.", 10.5)}</div></div>')
    stats = section("Upgrade Stats", [statrow("people", "Wood Gathering places", "4", "5", False), statrow("road", "Road", "+10% work").replace("+10% work", "+10% work").replace('color:#fff;">+10% work', 'color:#B6F25A;">+10% work')],
                    extra_head=t("Lv.3/10", 11, "#D7F25A"), style="margin-top:6px")
    faces = ["face_m1", "face_f1", "face_m2", "face_f3"]
    cell = (330 - 32) / 5
    grid = "".join(f'<div class="col" style="align-items:center;width:{cell}px;flex:none">{slot(f, 38, "slot")}{t(n, 9.5)}</div>'
                   for f, n in zip(faces, ["Aldo", "Mira", "Tomas", "Lena"]))
    grid += f'<div class="col" style="align-items:center;width:{cell}px;flex:none">{slot("lock", 38, "slot", dim=True, share=.6)}{t("Lv.4", 9.5, "#F3E6D2")}</div>'
    stepper = (f'<div class="row c" style="margin:4px 0 3px">{sq("red", "minus", 28)}'
               f'{box("capsule", f"<div class=row style=height:100%;padding:0_10px;gap:4px>{icon(chr(117)+chr(115)+chr(101)+chr(114), 16)}{t(chr(52)+chr(47)+chr(52), 12.5)}</div>".replace("0_10px", "0 10px"), "height:24px;margin:0 -2px")}'
               f'{sq("grey", "plus", 28)}</div>')
    workers = section("Wood Gathering", [f'<div class="row" style="padding-top:5px;align-items:flex-start">{grid}</div>',
                                         f'<div style="margin-top:3px">{t("Wood 1.1/m each · 4.4/m total", 10.5, "#F3E6D2")}</div>', stepper],
                      extra_head=t("4/4", 11.5, "#D7F25A"), style="margin-top:6px")
    up = section("Upgrade to Lv.4", [reqrow(f'{icon("campfire", 16)}{t("Great Hearth level 30/4", 11.5)}', True, False),
                                     reqrow(cost("wood", "1.3K", "500", False, 11.5).replace('color:#fff">1.3K', 'color:#B6F25A">1.3K'), True),
                                     reqrow(cost("stone", "120", "300", True, 11.5), False)], style="margin-top:6px")
    foot = f'{sq("red", "trash", 36)}{btn("grey", "Upgrade", None, 36, cost("stone", "300", None, True, 10), 13)}'
    return hud(card_open=True) + card("Woodcutters' Camp", 3, head + stats + workers + up, foot), ""


# MARK: - Build strip

def page_build():
    tabs = "".join(box("tab_on" if i == 0 else "tab_off", f'<div class="row c" style="height:100%;padding:0 6px 2px;gap:3px">{icon(ic, 18)}{t(l, 11.5) if i == 0 else ""}</div>', "height:26px;flex:none")
                   for i, (ic, l) in enumerate((("build", "All"), ("home", ""), ("food", ""), ("pickaxe", ""), ("storage", ""), ("road", ""), ("fence", ""), ("lantern", ""), ("tree", ""), ("landmark", ""))))
    items = [("home", "Timber House", "rare", [("wood", "40"), ("stone", "10")], "1:30", "3", False, False),
             ("wheat", "Farm Field", "uncommon", [("wood", "25")], "0:45", None, False, False),
             ("axe", "Woodcutters' Camp", "common", [("wood", "30"), ("stone", "5")], "1:00", "2", False, False),
             ("storage", "Storage Pit", "legendary", [("wood", "60")], "2:00", None, True, False),
             ("sick", "Healer's Hut", "rare", [("wood", "45"), ("hides", "10")], "2:10", None, False, False),
             ("landmark", "Stone Chapel", "common", [], "", None, False, True),
             ("road", "Dirt Path", "epic", [("stone", "1")], "", None, False, False),
             ("lantern", "Brazier", "epic", [("wood", "6"), ("ore", "2")], "", None, False, False)]
    cards = ""
    for ic, name, rar, cst, tm, cnt, short, locked in items:
        fit = min(11, 76 / (len(name) * 0.29 + 0.01))
        cst_html = "".join(cost(i, v, None, short, 10) for i, v in cst) if not locked else t("Pottery", 10, "#E6C3FF")
        cards += (f'<div class="col" style="width:80px;align-items:center;gap:2px;flex:none">'
                  f'{slot(ic, 54, "common" if locked else rar, cnt and "×" + cnt, locked, dim=locked)}'
                  f'<div class="row c" style="height:24px;text-align:center">{t(name, fit, extra="white-space:normal;line-height:1")}</div>'
                  f'<div class="row c" style="gap:4px;height:15px">{cst_html}</div></div>')
    strip = (f'<div class="a col" style="left:{SL + 8}px;right:{SR + 8}px;bottom:{SB + 6}px;height:156px">'
             f'<div class="a sk-panel" style="inset:0"></div><div class="col" style="position:relative;height:100%">'
             f'<div class="row" style="margin:-6px -6px 0;gap:6px">{plank("Build", None, 15, "150px")}'
             f'<div class="row" style="gap:3px;margin-top:2px">{tabs}</div><div class="grow"></div>'
             f'{capsule(icon("build", 16) + t("1/3", 11.5), 22, "margin-right:30px")}</div>'
             f'<div class="a" style="right:-8px;top:-9px">{sq("red", "close", 30)}</div>'
             f'<div class="row" style="gap:6px;padding:8px 10px 0;overflow:hidden">{cards}</div></div></div>')
    return hud(panel_open=True) + strip, ""


# MARK: - People window (tabs, villagers with portraits)

def page_people():
    tabs = tab("Work", False, count="2 idle") + tab("Villagers", True, count="43") + tab("Wellbeing", False)
    rows = ""
    people = [("face_m1", "Aldo", "Wood Gathering", "Woodcutter", 4, .6, False), ("face_f1", "Mira", "Healing", "Healer", 6, .2, True),
              ("face_m2", "Tomas", "Construction", "Builder", 3, .8, False), ("face_f3", "Lena", "Farming", "Farmer", 2, .4, False),
              ("face_m3", "Joss", "Idle", "Forager", 1, .1, False), ("face_f2", "Ines", "Research", "Thinker", 5, .5, False)]
    cell = (620 - 20 - 6) / 2
    for f, n, job, skill, lv, fr, sick in people:
        extra = f'<div class="a" style="right:-4px;bottom:-2px">{icon("sick", 16)}</div>' if sick else ""
        sick_line = f'{t("Ill · under care", 10, "#B6F25A")}{bar(.4, "90px", 6)}' if sick else ""
        rows += (f'<div style="width:{cell}px;position:relative;margin-bottom:6px;min-height:50px">'
                 f'<div class="a sk-section" style="inset:0"></div><div class="row" style="position:relative;height:100%;min-height:50px;padding:5px 8px;gap:8px">'
                 f'<div style="position:relative">{slot(f, 38, "slot")}{extra}</div>'
                 f'<div class="col grow">{t(n, 12.5)}{t(job, 10.5, "#FFE07A" if job == "Idle" else "#F3E6D2")}{sick_line}</div>'
                 f'<div class="col" style="align-items:flex-end;gap:3px;width:84px">{t(f"{skill} <span style=color:#D7F25A>Lv.{lv}</span>", 10.5)}{bar(fr, "70px", 8, "gold")}</div></div></div>')
    body = f'<div class="row" style="flex-wrap:wrap;justify-content:space-between">{rows}</div>'
    return hud(panel_open=True) + window("People & Work", body, tabs), ""


# MARK: - Research window

def page_research():
    tabs = (f'<div class="grow">{t("18/23 discovered · Great Hearth Lv.12", 12, "#F3E6D2")}</div>'
            + capsule(icon("knowledge", 18) + t("231  +11.2/m", 12), 24))
    cols = ""
    for head, techs in (("Level 1–5", [("Improved Stone Tools", "done"), ("Food Preservation", "done"), ("Herbal Healing", "done")]),
                        ("Level 6–10", [("Permanent Settlement", "done"), ("Oral Tradition", "avail"), ("Longhouses", "lock")]),
                        ("Level 11–15", [("Agriculture", "prog"), ("Animal Husbandry", "lock"), ("Pottery", "lock")])):
        nodes = ""
        for name, st in techs:
            kind = {"done": "uncommon", "avail": "legendary", "prog": "rare", "lock": "common"}[st]
            ic = {"done": "check", "avail": "knowledge", "prog": "clock", "lock": "lock"}[st]
            sel = "outline:2.5px solid #fff;outline-offset:-1px;border-radius:9px;" if name == "Oral Tradition" else ""
            fit = min(11.5, 86 / (len(name) * 0.3))
            nodes += (f'<div style="position:relative;height:40px;margin-bottom:5px;{sel}"><div class="a sk-section" style="inset:0"></div>'
                      f'<div class="row" style="position:relative;height:100%;padding:0 5px;gap:5px">{slot(ic, 30, kind)}'
                      f'{t(name, fit, extra="white-space:normal;line-height:1")}</div></div>')
        cols += (f'<div class="col" style="width:124px;flex:none"><div class="row c" style="height:18px;gap:3px;margin-bottom:4px">'
                 f'{icon("flame", 14)}{t(head, 11)}</div>{nodes}</div>')
    detail = (f'<div style="position:relative;width:196px;flex:none"><div class="a sk-section" style="inset:0"></div>'
              f'<div class="col" style="position:relative;padding:7px 8px;gap:4px"><div class="row" style="gap:6px">{slot("knowledge", 40, "legendary")}'
              f'<div class="col grow">{t("Oral Tradition", 13)}<div class="row" style="gap:3px">{icon("clock", 13)}{t("4:00", 10.5)}</div></div></div>'
              f'{ink("Stories by the fire keep what the elders know.", 10.5)}'
              f'{t("+15% Research · unlocks Storyteller", 10.5, "#B6F25A", "white-space:normal")}'
              f'<div class="row" style="gap:5px;margin-top:3px">{cost("knowledge", "231", "120")}</div>'
              f'{btn("green", "Research", None, 34, None, 13)}</div></div>')
    body = f'<div class="row" style="gap:6px;align-items:flex-start">{cols}{detail}</div>'
    return hud(panel_open=True) + window("Research", body, tabs), ""


# MARK: - Goals (Chapter Tasks, bottom tabs)

def page_goals():
    banner = (f'<div class="row" style="min-height:64px;margin-bottom:6px;border-radius:9px;border:2px solid #3A2314;padding:8px 10px;gap:8px;'
              f'background:linear-gradient(90deg,#4A2E1A,#8A5A32)">{icon("campfire", 44)}'
              f'<div class="col grow">{t("Chapter 4 · Bronze Age", 11, "#FFE07A")}{t("The Market Town", 15)}{t("Grow the village into a town that trades", 10.5, "#F3E6D2")}</div>'
              f'{btn("orange", "Story", 92, 32, None, 12.5, "legacy")}</div>')
    tasks = ""
    for title, done in (("Population 43/50", False), ("3 Timber Houses", True), ("Research Pottery", False)):
        right = icon("check", 24) if done else btn("blue", "Go", 52, 28, None, 13)
        pill = capsule(t("1/1" if done else "0/1", 11, "#B6F25A" if done else "#fff"), 20)
        tasks += (f'<div style="position:relative;min-height:38px;margin-bottom:5px"><div class="a sk-section" style="inset:0"></div>'
                  f'<div class="row" style="position:relative;min-height:38px;padding:0 8px;gap:6px"><div class="grow">{t(title, 12, "#B6F25A" if done else "#fff")}</div>'
                  f'{pill}<div style="width:58px" class="row c">{right}</div></div></div>')
    road = section("The road ahead", [f'<div class="row" style="height:23px;gap:6px;{"border-top:1px solid #CFA878;" if i else ""}">{icon(ic, 16)}<div class="grow">{t(n, 11.5, c)}</div>{t(e, 10.5, "#F3E6D2")}</div>'
                                      for i, (ic, n, c, e) in enumerate((("check", "1. The First Fire", "#fff", "Stone Age"), ("check", "2. Bronze Dawn", "#fff", "Bronze Age"), ("flame", "4. The Market Town", "#FFE07A", "Bronze Age")))],
                   style="margin-top:4px")
    body = banner + tasks + road
    btabs = (f'<div class="a row" style="left:{(W - 620) / 2 + 14}px;bottom:{SB + 4}px;gap:4px;align-items:flex-end">'
             + tab("Chapter", True, 112, 26) + tab("Milestones", False, 112, 26, "!") + tab("First Steps", False, 112, 26) + "</div>")
    return hud(panel_open=True) + window("Chapter Tasks", body, None, w=620, bottom=SB + 32) + btabs, ""


# MARK: - Event decision (modal)

def page_event():
    scrim = '<div class="a" style="inset:0;background:rgba(26,15,8,.45)"></div>'
    left = (f'<div class="col" style="width:210px;flex:none;gap:5px"><div class="row c" style="height:70px;border-radius:9px;border:2px solid #3A2314;'
            f'background:linear-gradient(180deg,#8A5A32,#4A2E1A)">{icon("campfire", 52)}</div>'
            f'{ink("A trader&#39;s cart lost a wheel by the ford. He offers salt for help, but the river is rising.", 11)}'
            f'<div class="row" style="gap:4px;align-items:flex-start">{icon("legacy", 16)}{ink("Your choice is written into the Legacy Timeline.", 10, "#8A6038")}</div></div>')
    opts = ""
    for i, (label, sub, c) in enumerate((("Help him across the ford", "+20 food later, the river may take a cart", [("wood", "10")]),
                                          ("Send him away", "Nothing gained, nothing lost", []))):
        on = i == 0
        cs = "".join(cost(a, b) for a, b in c)
        opts += (f'<div style="position:relative;margin-bottom:6px"><div class="a sk-{"slot" if on else "section"}" style="inset:0"></div>'
                 f'<div class="col" style="position:relative;padding:7px 9px {10 if on else 7}px">'
                 f'<div class="row" style="gap:6px">{icon("check" if on else "next", 20 if on else 18)}<div class="grow">{t(label, 12.5)}</div>{cs}</div>'
                 f'<div style="margin-left:26px">{t(sub, 10.5, "#F3E6D2")}</div></div></div>')
    body = f'<div class="row" style="gap:10px;align-items:flex-start">{left}<div class="col grow">{opts}</div></div>'
    foot = btn("blue", "Decide later", None, 36, None, 13) + btn("green", "Confirm", None, 36, None, 13)
    return hud(panel_open=True) + scrim + dialog("The Stranded Trader", body, foot), ""


def page_welcome():
    scrim = '<div class="a" style="inset:0;background:rgba(26,15,8,.45)"></div>'
    cells = "".join(reward(i, k, a, 46) for i, k, a in (("food", "uncommon", "+320"), ("wood", "legendary", "+1.2K"), ("stone", "common", "+410"),
                                                        ("ore", "rare", "+38"), ("hides", "legendary", "+12"), ("knowledge", "epic", "+96")))
    body = (t("Your people kept the fire alive for 2 h 14 min.", 12.5) + ink("Time away counts for up to 8 hours.", 10.5)
            + section("Gathered", [f'<div class="row" style="gap:6px;padding:6px 0 5px">{cells}</div>'], style="margin-top:6px")
            + section("The village", [f'<div class="row" style="height:24px;gap:5px">{icon("check", 16)}{t("Two newcomers settled in", 11)}</div>',
                                      f'<div class="row" style="height:24px;gap:5px;border-top:1px solid #CFA878">{icon("check", 16)}{t("The Timber House was finished", 11)}</div>'],
                      style="margin-top:6px"))
    foot = btn("green", "Back to the village", 240, 38, None, 14)
    return hud(panel_open=True) + scrim + dialog("Welcome back, Founder", body, foot, 560), ""


def reward(ic, kind, amount, size=36):
    """GameUi.RewardCell: a rarity cell, the icon high, the amount at the bottom."""
    return box(f"cell_{kind}", f'<div class="col" style="height:100%;align-items:center;padding-top:3px">{icon(ic, size * 0.6)}</div>'
               f'<div class="a row c" style="left:0;right:0;bottom:3px">{t(amount, 9.5)}</div>', f"width:{size}px;height:{size}px;flex:none")


def page_people_work():
    tabs = tab("Work", True, count="2 idle") + tab("Villagers", False, count="43") + tab("Wellbeing", False)
    stats = "".join(f'<div class="grow" style="position:relative;height:42px"><div class="a sk-section" style="inset:0"></div><div class="row" style="position:relative;height:100%;padding:0 8px;gap:6px">'
                    f'{icon(i, 24)}<div class="col">{t(v, 15, c)}{t(l, 10, "#F3E6D2")}</div></div></div>'
                    for i, v, l, c in (("people", "43/105", "People / beds", "#fff"), ("user", "2", "Idle", "#FFE07A"), ("food", "+4.2/m", "Food", "#B6F25A")))
    jobs = ""
    for i, (ic, name, eff, w, cap) in enumerate((("food", "Foraging", "Food 1.4/m each · 8.4/m total", 6, 8), ("axe", "Wood Gathering", "Wood 1.1/m each · 5.5/m total", 5, 5),
                                                ("stone", "Stone Gathering", "Stone 0.8/m each · 2.4/m total", 3, 4), ("build", "Construction", "Build speed ×2.0", 4, 43))):
        can_add = w < cap
        jobs += (f'<div class="row" style="min-height:40px;gap:6px;{"border-top:1px solid #CFA878;" if i else ""}">{slot(ic, 30, "slot", share=.7)}'
                 f'<div class="col grow">{t(name, 12)}{t(eff, 10, "#F3E6D2")}</div>{sq("red", "minus", 28)}'
                 f'{box("capsule", f"<div class=row style=height:100%;justify-content:center>{t(f'{w}/{cap}', 12)}</div>", "width:62px;height:24px;margin:0 -2px")}'
                 f'{sq("green" if can_add else "grey", "plus", 28)}</div>')
    body = f'<div class="row" style="gap:6px;margin-bottom:6px">{stats}</div>' + section("Work", [jobs], extra_head=t("Places from workplaces", 10.5, "#F3E6D2"))
    return hud(panel_open=True) + window("People & Work", body, tabs), ""


# MARK: - Story (dialogue box with a portrait)

def page_story():
    scrim = '<div class="a" style="inset:0;background:linear-gradient(180deg,rgba(20,12,6,0),rgba(20,12,6,.55))"></div>'
    portrait = f'<div class="a" style="left:{SL + 18}px;bottom:{SB + 70}px">{icon("face_f1", 150)}</div>'
    text = ("The valley was green when we came, and the river ran clear. We were few, and the nights were long. "
            "Now the fire must grow into a hearth, and the camp into a village.")
    boxh = (f'<div class="a col" style="left:{SL + 120}px;right:{SR + 30}px;bottom:{SB + 10}px;height:170px">'
            f'<div class="a sk-panel" style="inset:0"></div><div class="col" style="position:relative;padding:12px 18px 10px 70px;gap:6px">'
            f'<div class="row" style="gap:8px">{t("Chapter 2 · Bronze Age", 11, "#F3E6D2")}</div>{t("Bronze Dawn", 17, "#FFE07A")}'
            f'{t(text, 12.5, extra="white-space:normal;line-height:1.2")}'
            f'<div style="position:relative"><div class="a sk-section" style="inset:0"></div><div class="row" style="position:relative;padding:5px 8px;gap:6px">{icon("goals", 18)}{t("Raise the hearth to level 10 and house 30 people", 11.5, "#FFE07A")}</div></div></div>'
            f'<div class="a" style="right:14px;bottom:16px">{icon("next", 18, "transform:rotate(90deg)")}</div>'
            f'<div class="a" style="right:44px;bottom:8px">{btn("green", "Continue", 120, 32, None, 13)}</div></div>')
    return hud(panel_open=True) + scrim + boxh + portrait, ""


PAGES = {
    "01_hud": page_hud, "01b_hud_693": lambda: page_hud(True), "02_heart_card": page_heart, "03_workplace_card": page_workplace,
    "04_build": page_build, "05_people": page_people, "06_research": page_research, "07_goals": page_goals,
    "08_event": page_event, "09_story": page_story, "10_welcome_back": page_welcome, "05b_people_work": page_people_work,
}


def main():
    out = sys.argv[1] if len(sys.argv) > 1 else os.path.join(HERE, "png")
    bg = sys.argv[2] if len(sys.argv) > 2 else None
    os.makedirs(out, exist_ok=True)
    if bg:
        Image.open(bg).convert("RGB").resize((1688, 780), Image.LANCZOS).save(os.path.join(HERE, "bg.jpg"), quality=82)
    with open(os.path.join(HERE, "kit.css"), "w") as f:
        f.write(css())
    for name, fn in PAGES.items():
        html, body_style = fn()
        doc = (f'<!doctype html><html><head><meta charset="utf-8"><link rel="stylesheet" href="kit.css"></head>'
               f'<body style="{body_style}">{html}</body></html>')
        path = os.path.join(HERE, name + ".html")
        with open(path, "w") as f:
            f.write(doc)
        render_page(path, os.path.join(out, name + ".png"), 693 if "693" in name else W)
        print("mock:", name)


def render_page(path, png, width):
    import subprocess
    import tempfile
    with tempfile.TemporaryDirectory() as tmp:
        shot = os.path.join(tmp, "s.png")
        subprocess.run([kit.chromium(), "--headless", "--no-sandbox", "--disable-gpu", "--hide-scrollbars", "--allow-file-access-from-files",
                        f"--window-size={width},{H}", "--force-device-scale-factor=2", "--virtual-time-budget=4000", f"--screenshot={shot}", "file://" + path],
                       check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        Image.open(shot).convert("RGB").crop((0, 0, width * 2, H * 2)).save(png)


if __name__ == "__main__":
    main()
