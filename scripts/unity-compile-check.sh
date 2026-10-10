#!/usr/bin/env bash
# Compiles all Unity scripts (unity/Assets) against the real UnityEngine/UnityEditor DLLs
# without a Unity install or license. The DLLs are streamed once from the public GameCI
# editor image (layer ~5 GB, only Editor/Data/Managed is kept, ~55 MB) into a cache dir.
# Usage: scripts/unity-compile-check.sh [cache-dir]
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
VERSION="$(sed -n 's/^m_EditorVersion: //p' "$ROOT/unity/ProjectSettings/ProjectVersion.txt")"
CACHE="${1:-${TMPDIR:-/tmp}/unity-managed-$VERSION}"
MANAGED="$CACHE/opt/unity/Editor/Data/Managed"

if [ ! -f "$MANAGED/UnityEngine/UnityEngine.CoreModule.dll" ]; then
  echo "Fetching Unity $VERSION managed DLLs into $CACHE ..."
  mkdir -p "$CACHE"
  IMAGE="unityci/editor"
  TAG="ubuntu-$VERSION-base-3"
  TOKEN=$(curl -s "https://auth.docker.io/token?service=registry.docker.io&scope=repository:$IMAGE:pull" | python3 -c "import json,sys;print(json.load(sys.stdin)['token'])")
  ACCEPT="application/vnd.oci.image.index.v1+json, application/vnd.docker.distribution.manifest.list.v2+json, application/vnd.oci.image.manifest.v1+json, application/vnd.docker.distribution.manifest.v2+json"
  INDEX=$(curl -s -H "Authorization: Bearer $TOKEN" -H "Accept: $ACCEPT" "https://registry-1.docker.io/v2/$IMAGE/manifests/$TAG")
  DIGEST=$(echo "$INDEX" | python3 -c "import json,sys;d=json.load(sys.stdin);print(next(m['digest'] for m in d['manifests'] if m.get('platform',{}).get('architecture')=='amd64'))")
  LAYER=$(curl -s -H "Authorization: Bearer $TOKEN" -H "Accept: $ACCEPT" "https://registry-1.docker.io/v2/$IMAGE/manifests/$DIGEST" \
    | python3 -c "import json,sys;d=json.load(sys.stdin);print(max(d['layers'],key=lambda l:l['size'])['digest'])")
  curl -sL -H "Authorization: Bearer $TOKEN" "https://registry-1.docker.io/v2/$IMAGE/blobs/$LAYER" \
    | tar -xz -C "$CACHE" --wildcards '*Editor/Data/Managed/*.dll'
fi

# Like Unity, drop engine modules whose com.unity.modules.* package is not in the manifest.
python3 - "$ROOT/unity/Packages/manifest.json" "$MANAGED/UnityEngine" "$ROOT/unity/CompileCheck/Modules.props" <<'PY'
import json, os, sys
manifest, managed, out = sys.argv[1:4]
deps = json.load(open(manifest))["dependencies"]
enabled = {k.split(".")[-1] for k in deps if k.startswith("com.unity.modules.")}
packaged = {"accessibility", "ai", "androidjni", "animation", "assetbundle", "audio", "cloth", "director",
            "imageconversion", "imgui", "jsonserialize", "particlesystem", "physics", "physics2d", "screencapture",
            "terrain", "terrainphysics", "tilemap", "ui", "uielements", "umbra", "unityanalytics", "unitywebrequest",
            "unitywebrequestassetbundle", "unitywebrequestaudio", "unitywebrequesttexture", "unitywebrequestwww",
            "vehicles", "video", "vr", "wind", "xr", "adaptiveperformance", "nvidia", "subsystems"}
removed = []
for f in sorted(os.listdir(managed)):
    if f.startswith("UnityEngine.") and f.endswith("Module.dll"):
        name = f[len("UnityEngine."):-len("Module.dll")].lower()
        if name in packaged and name not in enabled:
            removed.append(os.path.join(managed, f))
with open(out, "w") as fh:
    fh.write("<Project><ItemGroup>\n")
    for r in removed:
        fh.write(f'  <Reference Remove="{r}" />\n')
    fh.write("</ItemGroup></Project>\n")
print(f"engine modules excluded (not in manifest): {len(removed)}")
PY
dotnet build "$ROOT/unity/CompileCheck" -nologo -v q -p:UnityManaged="$MANAGED"
