#!/usr/bin/env bash
# Build the plugin in Debug and install it into the dev Jellyfin container.
# Intended to be run from inside the dev container.
#
#   ./scripts/deploy.sh            # -> Jellyfin 12.0 (net10.0) on :8096
#   ./scripts/deploy.sh --legacy   # -> Jellyfin 10.11 (net9.0) on :8097
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

PROJECT="Jellyfin.Plugin.AppleMusic/Jellyfin.Plugin.AppleMusic.csproj"
PLUGIN_NAME="Jellyfin.Plugin.AppleMusic"

if [ "${1:-}" = "--legacy" ]; then
  TFM="net9.0"
  CONFIG_DIR="${JELLYFIN_LEGACY_CONFIG_DIR:-/jellyfin/config-legacy}"
  CONTAINER="${JELLYFIN_LEGACY_CONTAINER:-applemusic-jellyfin-legacy}"
  LABEL="Jellyfin 10.11"
else
  TFM="net10.0"
  CONFIG_DIR="${JELLYFIN_CONFIG_DIR:-/jellyfin/config}"
  CONTAINER="${JELLYFIN_CONTAINER:-applemusic-jellyfin}"
  LABEL="Jellyfin 12.0"
fi

VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Directory.Build.props | head -1)"
TARGET="$CONFIG_DIR/plugins/${PLUGIN_NAME}_${VERSION}"

echo "==> building $TFM for $LABEL"
dotnet publish "$PROJECT" \
  --configuration Debug \
  --framework "$TFM" \
  --output "$ROOT/bin/deploy/$TFM" \
  /property:GenerateFullPaths=true \
  /consoleloggerparameters:NoSummary

echo "==> installing into $TARGET"
mkdir -p "$TARGET"
rm -f "$TARGET"/*.dll "$TARGET"/*.pdb
cp "$ROOT/bin/deploy/$TFM/${PLUGIN_NAME}.dll" "$TARGET/"
cp "$ROOT/bin/deploy/$TFM/${PLUGIN_NAME}.pdb" "$TARGET/" 2>/dev/null || true

if command -v docker >/dev/null 2>&1 && docker inspect "$CONTAINER" >/dev/null 2>&1; then
  echo "==> restarting $CONTAINER"
  docker restart "$CONTAINER" >/dev/null
  echo "    $LABEL is restarting; the plugin will be loaded on startup"
else
  echo "    container '$CONTAINER' not reachable — restart Jellyfin manually to load the plugin"
fi
