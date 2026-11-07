#!/usr/bin/env bash
# Builds dist/CatapultRework-<version>.zip in Thunderstore layout (works with r2modman / manual install).
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
VER="$(python3 -c "import json;print(json.load(open('$ROOT/package/manifest.json'))['version_number'])")"
STAGE="$ROOT/dist/stage"
rm -rf "$STAGE" && mkdir -p "$STAGE/plugins"
cp "$ROOT/src/CatapultRework/bin/Release/CatapultRework.dll" "$STAGE/plugins/"
cp "$ROOT/package/manifest.json" "$ROOT/package/icon.png" "$STAGE/"
cp "$ROOT/README.md" "$STAGE/"
[ -f "$ROOT/CHANGELOG.md" ] && cp "$ROOT/CHANGELOG.md" "$STAGE/"
(cd "$STAGE" && python3 -c "
import zipfile,os
z=zipfile.ZipFile('../CatapultRework-$VER.zip','w',zipfile.ZIP_DEFLATED)
for d,_,fs in os.walk('.'):
    for f in fs:
        p=os.path.join(d,f); z.write(p,os.path.relpath(p,'.'))
")
rm -rf "$STAGE"
echo "dist/CatapultRework-$VER.zip"
