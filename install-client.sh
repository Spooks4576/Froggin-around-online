#!/usr/bin/env bash

set -euo pipefail

SERVER_HOST="${SERVER_HOST:-1.1.1.1}"
SERVER_PORT="${SERVER_PORT:-27015}"
BEPINEX_VERSION="${BEPINEX_VERSION:-5.4.23.2}"
BEPINEX_URL="https://github.com/BepInEx/BepInEx/releases/download/v${BEPINEX_VERSION}/BepInEx_win_x64_${BEPINEX_VERSION}.zip"

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# -------- helpers --------

need() {
  command -v "$1" >/dev/null 2>&1 || {
    echo "error: missing dependency '$1'. install it first." >&2
    exit 1
  }
}

# -------- locate game --------

find_game_dir() {
  local candidates=(
    "$HOME/.steam/debian-installation/steamapps/common/Froggin' Around Demo"
    "$HOME/.steam/steam/steamapps/common/Froggin' Around Demo"
    "$HOME/.local/share/Steam/steamapps/common/Froggin' Around Demo"
  )
  # Plus any configured Steam library folders.
  local libs=(
    "$HOME/.steam/debian-installation/steamapps/libraryfolders.vdf"
    "$HOME/.local/share/Steam/steamapps/libraryfolders.vdf"
  )
  for vdf in "${libs[@]}"; do
    [[ -f "$vdf" ]] || continue
    while IFS= read -r path; do
      candidates+=("$path/steamapps/common/Froggin' Around Demo")
    done < <(grep -oE '"path"[[:space:]]+"[^"]+"' "$vdf" | sed -E 's/.*"([^"]+)"/\1/')
  done
  for dir in "${candidates[@]}"; do
    if [[ -f "$dir/Froggin Around.exe" ]]; then
      echo "$dir"; return 0
    fi
  done
  return 1
}

GAME_DIR="${GAME_DIR:-}"
if [[ -z "$GAME_DIR" ]]; then
  GAME_DIR=$(find_game_dir) || {
    echo "error: could not auto-detect 'Froggin' Around Demo'. Pass GAME_DIR=..." >&2
    exit 1
  }
fi

if [[ ! -f "$GAME_DIR/Froggin Around.exe" ]]; then
  echo "error: '$GAME_DIR' does not look like a Froggin' Around install." >&2
  exit 1
fi

MANAGED_DIR="$GAME_DIR/Froggin Around_Data/Managed"
[[ -d "$MANAGED_DIR" ]] || { echo "error: $MANAGED_DIR missing." >&2; exit 1; }

echo "→ Game dir:   $GAME_DIR"
echo "→ Server:     $SERVER_HOST:$SERVER_PORT"

# -------- deps --------

need dotnet
need curl
need unzip

# -------- build plugin --------

echo "→ Building FrogOnline plugin (Release)…"
dotnet build "$REPO_ROOT/FrogOnline/FrogOnline.csproj" \
  -c Release \
  -p:ManagedDir="$MANAGED_DIR" \
  --nologo -v minimal >/dev/null

PLUGIN_OUT="$REPO_ROOT/FrogOnline/bin/Release"
for f in FrogOnline.dll FrogOnline.Shared.dll LiteNetLib.dll; do
  [[ -f "$PLUGIN_OUT/$f" ]] || { echo "error: build output missing $f"; exit 1; }
done

# -------- install BepInEx --------

if [[ ! -f "$GAME_DIR/winhttp.dll" || ! -d "$GAME_DIR/BepInEx" ]]; then
  echo "→ Installing BepInEx $BEPINEX_VERSION…"
  TMP=$(mktemp -d)
  trap 'rm -rf "$TMP"' EXIT
  curl -fL --progress-bar -o "$TMP/bepinex.zip" "$BEPINEX_URL"
  unzip -q -o "$TMP/bepinex.zip" -d "$GAME_DIR"
else
  echo "→ BepInEx already present."
fi

# -------- deploy plugin --------

PLUGIN_DIR="$GAME_DIR/BepInEx/plugins/FrogOnline"
mkdir -p "$PLUGIN_DIR"
echo "→ Copying plugin to $PLUGIN_DIR"
cp -f "$PLUGIN_OUT/FrogOnline.dll"        "$PLUGIN_DIR/"
cp -f "$PLUGIN_OUT/FrogOnline.Shared.dll" "$PLUGIN_DIR/"
cp -f "$PLUGIN_OUT/LiteNetLib.dll"        "$PLUGIN_DIR/"

# -------- seed config --------

mkdir -p "$GAME_DIR/BepInEx/config"
CFG="$GAME_DIR/BepInEx/config/org.froggin.online.cfg"
cat > "$CFG" <<EOF
[Net]
Port = $SERVER_PORT
TickRate = 30
LastHostAddress = $SERVER_HOST
EOF
echo "→ Wrote $CFG"

# -------- done --------

cat <<MSG

================================================================
  FrogOnline installed.

  Steam launch options (REQUIRED for Proton to load BepInEx):

    WINEDLLOVERRIDES="winhttp=n,b" %command%

  Set this in Steam → Froggin' Around Demo → Properties →
  General → Launch Options, then launch the game and press F9
  to open the lobby overlay.
================================================================
MSG
