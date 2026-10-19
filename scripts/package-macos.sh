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
ICNS="$BUILD_DIR/AppIcon.icns"

rm -rf "$PUBLISH_DIR"

echo "==> Publishing ($RID, v$VERSION)"
dotnet publish "$ROOT/Lapsus/Lapsus.csproj" -c Release -r "$RID" --self-contained \
  -p:PublishSingleFile=false -o "$PUBLISH_DIR"

echo "==> Generating icon from Assets/icon-light.svg"
"$ROOT/scripts/make-macos-icon.sh" "$BUILD_DIR" "$ICNS"

echo "==> vpk pack"
# "-" binds the signature to $BUNDLE_ID; otherwise macOS sees "apphost" and an Accessibility
# grant never reaches CGEventPost. A Developer ID (LAPSUS_MAC_APP_IDENTITY) keeps that grant
# stable across builds.
SIGN_ARGS=(--signAppIdentity "-")
if [ -n "${LAPSUS_MAC_APP_IDENTITY:-}" ]; then
  SIGN_ARGS=(--signAppIdentity "$LAPSUS_MAC_APP_IDENTITY")
  [ -n "${LAPSUS_MAC_INSTALL_IDENTITY:-}" ] && SIGN_ARGS+=(--signInstallIdentity "$LAPSUS_MAC_INSTALL_IDENTITY")
  [ -n "${LAPSUS_NOTARY_PROFILE:-}" ] && SIGN_ARGS+=(--notaryProfile "$LAPSUS_NOTARY_PROFILE")
  [ -n "${LAPSUS_KEYCHAIN:-}" ] && SIGN_ARGS+=(--keychain "$LAPSUS_KEYCHAIN")
else
  echo "    (no LAPSUS_MAC_APP_IDENTITY: ad-hoc signature, not notarized)"
fi

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
  "${SIGN_ARGS[@]}" \
  --yes

echo "==> Building disk image"
# The drag-to-Applications .dmg most Mac users expect, beside vpk's .pkg. The .app comes out of the
# portable zip rather than $PUBLISH_DIR: that bundle is the one vpk signed and gave its update
# metadata, so a copy installed from the image updates itself like one installed from the .pkg.
# ditto, not unzip: it keeps the bundle's extended attributes and signature intact.
DMG_STAGE="$BUILD_DIR/dmg"
DMG="$OUTPUT_DIR/$APP_NAME-osx.dmg"
rm -rf "$DMG_STAGE" "$DMG"
mkdir -p "$DMG_STAGE"
ditto -x -k "$OUTPUT_DIR/$APP_NAME-osx-Portable.zip" "$DMG_STAGE"
APP_BUNDLE="$(find "$DMG_STAGE" -maxdepth 2 -name '*.app' -type d | head -n 1)"
if [ -z "$APP_BUNDLE" ]; then
  echo "No .app bundle inside $APP_NAME-osx-Portable.zip" >&2
  exit 1
fi
if [ "$(dirname "$APP_BUNDLE")" != "$DMG_STAGE" ]; then
  mv "$APP_BUNDLE" "$DMG_STAGE/"
fi
ln -s /Applications "$DMG_STAGE/Applications"
# hdiutil now and then fails with "Resource busy" on CI runners; a retry is the usual cure.
for attempt in 1 2 3; do
  if hdiutil create -volname "$APP_NAME" -srcfolder "$DMG_STAGE" -ov -format UDZO "$DMG" >/dev/null; then
    break
  fi
  [ "$attempt" = 3 ] && { echo "hdiutil create failed" >&2; exit 1; }
  sleep 5
done
rm -rf "$DMG_STAGE"

echo "==> Done: $OUTPUT_DIR"
echo "    Upload every file in that folder to a GitHub Release tagged v$VERSION."
