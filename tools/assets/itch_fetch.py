#!/usr/bin/env python3
"""Downloads the free ("name your own price", $0) files of an itch.io asset pack, the way the
"Download Now" -> "No thanks, just take me to the downloads" buttons do:

  GET  <game page>                                 -> csrf_token (form field) + session cookie
  POST <game page>/download_url  (csrf_token)      -> {"url": <download page>}
  GET  <download page>                             -> the uploads (data-upload_id + file name)
  POST <game page>/file/<upload_id>?source=game_download (csrf_token) -> {"url": <file>}

Usage (as a module): fetch(game_url, name_contains, dest_path)
"""
import http.cookiejar
import json
import os
import re
import urllib.parse
import urllib.request

UA = "Mozilla/5.0 (X11; Linux x86_64) founders-asset-build"


def _opener():
    jar = http.cookiejar.CookieJar()
    op = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(jar))
    op.addheaders = [("User-Agent", UA)]
    return op


def _csrf(page):
    m = re.search(r'name="csrf_token"\s+value="([^"]+)"', page) or re.search(r'csrf_token" value="([^"]+)"', page)
    if not m:
        raise RuntimeError("itch.io: no csrf_token on the page")
    return m.group(1)


def _post_json(op, url, csrf, referer):
    data = urllib.parse.urlencode({"csrf_token": csrf}).encode()
    req = urllib.request.Request(url, data=data, headers={"X-Requested-With": "XMLHttpRequest", "Referer": referer})
    with op.open(req) as r:
        return json.loads(r.read().decode())


def uploads(game_url):
    """[(upload_id, file name)] of the pack's free files, plus the opener and token to fetch them."""
    op = _opener()
    game_url = game_url.rstrip("/")
    page = op.open(game_url).read().decode("utf-8", "replace")
    csrf = _csrf(page)
    dl_page_url = _post_json(op, game_url + "/download_url", csrf, game_url)["url"]
    dl = op.open(dl_page_url).read().decode("utf-8", "replace")
    csrf = re.search(r'csrf_token" value="([^"]+)"', dl).group(1) if "csrf_token" in dl else csrf
    found = []
    for m in re.finditer(r'data-upload_id="(\d+)"', dl):
        tail = dl[m.end():m.end() + 2500]
        name = re.search(r'class="name"[^>]*title="([^"]+)"', tail) or re.search(r'class="name"[^>]*>([^<]+)<', tail)
        found.append((m.group(1), name.group(1).strip() if name else ""))
    return op, csrf, game_url, dl_page_url, found


def fetch(game_url, name_contains, dest):
    """Downloads the first free upload whose file name contains `name_contains` to `dest`."""
    if os.path.exists(dest) and os.path.getsize(dest) > 0:
        return dest
    op, csrf, game_url, referer, found = uploads(game_url)
    match = [u for u in found if name_contains.lower() in u[1].lower()]
    if not match:
        raise RuntimeError(f"itch.io: no upload matching {name_contains!r} in {found}")
    uid = match[0][0]
    url = _post_json(op, f"{game_url}/file/{uid}?source=game_download", csrf, referer)["url"]
    os.makedirs(os.path.dirname(dest), exist_ok=True)
    tmp = dest + ".part"
    with op.open(url) as r, open(tmp, "wb") as f:
        while True:
            chunk = r.read(1 << 20)
            if not chunk:
                break
            f.write(chunk)
    os.replace(tmp, dest)
    return dest


if __name__ == "__main__":
    import sys
    print(uploads(sys.argv[1])[4])
