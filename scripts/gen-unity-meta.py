#!/usr/bin/env python3
"""Create missing Unity .meta files under unity/Assets with stable random GUIDs.

Lets us add scripts/folders without opening the Unity editor (e.g. from a cloud
session). Existing .meta files are never touched.  Usage: python3 scripts/gen-unity-meta.py
"""
import os
import sys
import uuid

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "unity", "Assets")
SKIP = {"Generated"}

FOLDER = """fileFormatVersion: 2
guid: {guid}
folderAsset: yes
DefaultImporter:
  externalObjects: {{}}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
"""

SCRIPT = """fileFormatVersion: 2
guid: {guid}
MonoImporter:
  externalObjects: {{}}
  serializedVersion: 2
  defaultReferences: []
  executionOrder: 0
  icon: {{instanceID: 0}}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
"""

TEXT = """fileFormatVersion: 2
guid: {guid}
TextScriptImporter:
  externalObjects: {{}}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
"""

ASMDEF = """fileFormatVersion: 2
guid: {guid}
AssemblyDefinitionImporter:
  externalObjects: {{}}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
"""

DEFAULT = """fileFormatVersion: 2
guid: {guid}
DefaultImporter:
  externalObjects: {{}}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
"""


def template(path):
    if os.path.isdir(path):
        return FOLDER
    ext = os.path.splitext(path)[1].lower()
    if ext == ".cs":
        return SCRIPT
    if ext == ".asmdef":
        return ASMDEF
    if ext in (".json", ".txt", ".md", ".csv", ".xml", ".bytes"):
        return TEXT
    return DEFAULT


def main():
    created = 0
    for dirpath, dirnames, filenames in os.walk(ROOT):
        dirnames[:] = [d for d in dirnames if d not in SKIP and not d.startswith(".")]
        for name in dirnames + filenames:
            if name.endswith(".meta") or name.startswith("."):
                continue
            path = os.path.join(dirpath, name)
            meta = path + ".meta"
            if not os.path.exists(meta):
                with open(meta, "w", newline="\n") as f:
                    f.write(template(path).format(guid=uuid.uuid4().hex))
                created += 1
                print("created", os.path.relpath(meta, ROOT))
    print(f"{created} meta file(s) created")
    return 0


if __name__ == "__main__":
    sys.exit(main())
