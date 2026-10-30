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

echo "==> Writing Info.plist"
# vpk's own Info.plist carries no usage descriptions, and macOS ends an app that opens the microphone
# without NSMicrophoneUsageDescription. So the plist is written here: the keys vpk writes (same values,
# from OsxBundleCommandRunner) plus the three descriptions of what Lapsus asks for.
PLIST="$BUILD_DIR/Info.plist"
cat > "$PLIST" <<PLIST_END
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key>
    <string>$APP_NAME</string>
    <key>CFBundleExecutable</key>
    <string>$APP_NAME</string>
    <key>CFBundleIdentifier</key>
    <string>$BUNDLE_ID</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleShortVersionString</key>
    <string>${VERSION%%-*}</string>
    <key>CFBundleVersion</key>
    <string>$VERSION</string>
    <key>CFBundleSignature</key>
    <string>????</string>
    <key>CFBundleIconFile</key>
    <string>$(basename "$ICNS")</string>
    <key>NSHighResolutionCapable</key>
    <true/>
    <key>NSAccessibilityUsageDescription</key>
    <string>Lapsus needs Accessibility access to capture keystrokes, find the text cursor, and type corrected text in other apps.</string>
    <key>NSInputMonitoringUsageDescription</key>
    <string>Lapsus needs Input Monitoring to receive keystrokes system-wide.</string>
    <key>NSMicrophoneUsageDescription</key>
    <string>Lapsus listens only while voice typing is on and you have pressed its hotkey, and turns your speech into text on this Mac.</string>
</dict>
</plist>
PLIST_END

echo "==> vpk pack"
# "-" binds the signature to $BUNDLE_ID (taken from the Info.plist above: vpk refuses --bundleId
# together with --plist); otherwise macOS sees "apphost" and an Accessibility grant never reaches
# CGEventPost. A Developer ID (LAPSUS_MAC_APP_IDENTITY) keeps that grant stable across builds.
#
# The entitlements are vpk's own .NET defaults plus the microphone (voice typing); see the file.
ENTITLEMENTS="$ROOT/Lapsus/Packaging/Mac/Lapsus.entitlements"
SIGN_ARGS=(--signAppIdentity "-" --signEntitlements "$ENTITLEMENTS")
if [ -n "${LAPSUS_MAC_APP_IDENTITY:-}" ]; then
  SIGN_ARGS=(--signAppIdentity "$LAPSUS_MAC_APP_IDENTITY" --signEntitlements "$ENTITLEMENTS")
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
  --icon "$ICNS" \
  --plist "$PLIST" \
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
