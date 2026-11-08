#!/usr/bin/env bash
# Downloads the managed DLLs of the free Valheim Dedicated Server (Steam app 896660)
# with an anonymous login, so the mod can be built without owning or installing the game.
# Result: ./game/valheim_server_Data/Managed/*.dll   (use -p:VALHEIM_INSTALL=$PWD/game)
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="${1:-$ROOT/game}"
TOOLS="$ROOT/.tools"
mkdir -p "$TOOLS" "$OUT"

if [ ! -x "$TOOLS/DepotDownloader/DepotDownloader" ]; then
  # Pinned release (override with DD_TAG=...). Retries guard against transient GitHub 5xx errors.
  TAG="${DD_TAG:-DepotDownloader_3.4.0}"
  if ! curl -fsSL --retry 5 --retry-delay 5 --retry-all-errors -o "$TOOLS/dd.zip" \
    "https://github.com/SteamRE/DepotDownloader/releases/download/$TAG/DepotDownloader-linux-x64.zip"; then
    if [ -f "$OUT/valheim_server_Data/Managed/assembly_valheim.dll" ]; then
      echo "::warning::Could not download DepotDownloader; using the DLLs already in $OUT (possibly from an older game version)"
      exit 0
    fi
    echo "Could not download DepotDownloader and no cached DLLs are present" >&2
    exit 1
  fi
  python3 -c "import zipfile,sys;zipfile.ZipFile(sys.argv[1]).extractall(sys.argv[2])" "$TOOLS/dd.zip" "$TOOLS/DepotDownloader"
  chmod +x "$TOOLS/DepotDownloader/DepotDownloader"
fi

echo 'regex:valheim_server_Data/Managed/.*\.dll' > "$TOOLS/filelist.txt"
"$TOOLS/DepotDownloader/DepotDownloader" -app 896660 -depot 896661 -filelist "$TOOLS/filelist.txt" -dir "$OUT"
ls "$OUT/valheim_server_Data/Managed/assembly_valheim.dll"
