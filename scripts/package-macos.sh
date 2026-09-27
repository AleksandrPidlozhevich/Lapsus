#!/usr/bin/env bash
set -euo pipefail

# Publishes Lapsus and hands it to `vpk pack` (Velopack's CLI), which builds the .app bundle, an
# installer, and the release feed that AppUpdater.cs / GithubSource reads to find updates. Upload
# everything under build/macos/Releases/ to a GitHub Release with matching tag "v<version>" — that
# release IS the update feed, there is nothing else to host.
#
# Usage: scripts/package-macos.sh [osx-arm64|osx-x64] [version]
#
# Requires the .NET SDK and the `vpk` global tool: dotnet tool install -g vpk

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
APP_NAME="Lapsus"
BUNDLE_ID="com.lapsus.app"

RID="${1:-osx-arm64}"
# Single source of truth: the repo-root VERSION file (also read by Directory.Build.props for the
# .NET assembly version, so this stays in sync with the Windows build without duplicating the number).
VERSION="${2:-$(cat "$ROOT/VERSION")}"

export PATH="$PATH:$HOME/.dotnet/tools"
if ! command -v vpk >/dev/null 2>&1; then
  echo "vpk not found. Install it with: dotnet tool install -g vpk" >&2
  exit 1
fi

BUILD_DIR="$ROOT/build/macos"
PUBLISH_DIR="$BUILD_DIR/publish-$RID"
OUTPUT_DIR="$BUILD_DIR/Releases"
ICONSET="$BUILD_DIR/AppIcon.iconset"
ICNS="$BUILD_DIR/AppIcon.icns"

rm -rf "$PUBLISH_DIR" "$ICONSET"

echo "==> Publishing ($RID, v$VERSION)"
dotnet publish "$ROOT/Lapsus/Lapsus.csproj" -c Release -r "$RID" --self-contained \
  -p:PublishSingleFile=false -o "$PUBLISH_DIR"

echo "==> Generating icon from Assets/icon-light.svg"
mkdir -p "$ICONSET"
BASE_PNG="$BUILD_DIR/icon-1024.png"
SVG="$ROOT/Lapsus/Assets/icon-light.svg"
# Rasterizing the SVG's rounded square edge-to-edge at the full 1024 canvas made the Dock icon look
# oversized next to every other app: macOS's own icon grid expects ~80% content with transparent
# margin around it (it adds its own shadow/mask in that margin). Render smaller and pad it back out.
CONTENT_PNG="$BUILD_DIR/icon-content.png"
sips -s format png "$SVG" --out "$CONTENT_PNG" --resampleWidth 824 >/dev/null
sips -p 1024 1024 "$CONTENT_PNG" --out "$BASE_PNG" >/dev/null
rm -f "$CONTENT_PNG"
for size in 16 32 128 256 512; do
  sips -z "$size" "$size" "$BASE_PNG" --out "$ICONSET/icon_${size}x${size}.png" >/dev/null
  double=$((size * 2))
  sips -z "$double" "$double" "$BASE_PNG" --out "$ICONSET/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns "$ICONSET" -o "$ICNS"

echo "==> vpk pack"
# --signAppIdentity -: without it vpk leaves the bundle with whatever ad-hoc signature dotnet
# publish gave the bare executable — unbound from Info.plist and identified as "apphost", not
# $BUNDLE_ID. macOS then can't reliably tie an Accessibility/Input Monitoring grant to the app,
# so CGEventPost text injection silently does nothing even after the user grants permission.
# --yes: re-running the same version locally while testing is the common case, and this only
# overwrites local build output (build/ is gitignored) — never anything already uploaded to GitHub.
vpk pack \
  --packId "$APP_NAME" \
  --packVersion "$VERSION" \
  --packDir "$PUBLISH_DIR" \
  --mainExe "$APP_NAME" \
  --packTitle "$APP_NAME" \
  --bundleId "$BUNDLE_ID" \
  --icon "$ICNS" \
  --outputDir "$OUTPUT_DIR" \
  --runtime "$RID" \
  --signAppIdentity "-" \
  --yes

echo "==> Done: $OUTPUT_DIR"
echo "    Upload every file in that folder to a GitHub Release tagged v$VERSION."
