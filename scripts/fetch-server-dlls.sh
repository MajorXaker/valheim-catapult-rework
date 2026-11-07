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
  TAG="$(curl -fsSL -o /dev/null -w '%{url_effective}' https://github.com/SteamRE/DepotDownloader/releases/latest | sed 's#.*/tag/##')"
  curl -fsSL -o "$TOOLS/dd.zip" "https://github.com/SteamRE/DepotDownloader/releases/download/$TAG/DepotDownloader-linux-x64.zip"
  python3 -c "import zipfile,sys;zipfile.ZipFile(sys.argv[1]).extractall(sys.argv[2])" "$TOOLS/dd.zip" "$TOOLS/DepotDownloader"
  chmod +x "$TOOLS/DepotDownloader/DepotDownloader"
fi

echo 'regex:valheim_server_Data/Managed/.*\.dll' > "$TOOLS/filelist.txt"
"$TOOLS/DepotDownloader/DepotDownloader" -app 896660 -depot 896661 -filelist "$TOOLS/filelist.txt" -dir "$OUT"
ls "$OUT/valheim_server_Data/Managed/assembly_valheim.dll"
