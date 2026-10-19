#!/usr/bin/env bash
set -euo pipefail

# Builds the macOS .icns from Lapsus/Assets/icon-light.svg. Shared by package-macos.sh (direct
# download) and package-mas.sh (Mac App Store).
#
# Usage: scripts/make-macos-icon.sh <work-dir> <output.icns>

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
WORK_DIR="$1"
ICNS="$2"

ICONSET="$WORK_DIR/AppIcon.iconset"
BASE_PNG="$WORK_DIR/icon-1024.png"
CONTENT_PNG="$WORK_DIR/icon-content.png"
SVG="$ROOT/Lapsus/Assets/icon-light.svg"

rm -rf "$ICONSET"
mkdir -p "$ICONSET"
# Rasterizing the SVG's rounded square edge-to-edge at the full 1024 canvas made the Dock icon look
# oversized next to every other app: macOS's own icon grid expects ~80% content with transparent
# margin around it (it adds its own shadow/mask in that margin). Render smaller and pad it back out.
sips -s format png "$SVG" --out "$CONTENT_PNG" --resampleWidth 824 >/dev/null
sips -p 1024 1024 "$CONTENT_PNG" --out "$BASE_PNG" >/dev/null
rm -f "$CONTENT_PNG"
for size in 16 32 128 256 512; do
  sips -z "$size" "$size" "$BASE_PNG" --out "$ICONSET/icon_${size}x${size}.png" >/dev/null
  double=$((size * 2))
  sips -z "$double" "$double" "$BASE_PNG" --out "$ICONSET/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns "$ICONSET" -o "$ICNS"
rm -rf "$ICONSET"
