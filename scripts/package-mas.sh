#!/usr/bin/env bash
set -euo pipefail

# Mac App Store build: publishes Lapsus with -p:MacAppStore=true (no Velopack), adds the StoreKit
# bridge, assembles the .app by hand, signs it with the App Sandbox entitlements and wraps it in the
# .pkg App Store Connect takes. Upload the .pkg with Transporter. Walked through in scripts/BUILDING.md ("Mac App Store").
#
# Usage:
#   scripts/package-mas.sh            store package; needs the variables below
#   scripts/package-mas.sh --test     sandboxed copy to run on this Mac; no profile, no .pkg
#
# Store package:
#   LAPSUS_MAS_APP_IDENTITY        "Apple Distribution: ..." (or "3rd Party Mac Developer Application: ...")
#   LAPSUS_MAS_INSTALLER_IDENTITY  "3rd Party Mac Developer Installer: ..."
#   LAPSUS_MAS_PROFILE             path to the Mac App Store provisioning profile (.provisionprofile)
#   LAPSUS_MAS_BUILD               optional build number (CFBundleVersion), defaults to the UTC
#                                  time of the build as YYYYMMDD.HHMM
#
# A store-signed app does not launch outside the store, so --test is how a sandboxed build is tried
# locally. It gets its own bundle id and name: macOS keeps privacy grants per bundle id, so the test
# copy has its own Accessibility / Input Monitoring entries and its own container
# (~/Library/Containers/<bundle id>), and never touches an installed Lapsus or its settings. Set
# LAPSUS_MAC_APP_IDENTITY (the Developer ID) to keep those grants across rebuilds; an ad-hoc
# signature changes with every build and macOS asks again each time.

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
EXECUTABLE="Lapsus"
RID="osx-arm64"
VERSION="$(cat "$ROOT/VERSION")"
ENTITLEMENTS_BASE="$ROOT/Lapsus/Packaging/Mas/Lapsus.entitlements"

TEST=false
[ "${1:-}" = "--test" ] && TEST=true

if $TEST; then
  APP_NAME="Lapsus Sandbox"
  BUNDLE_ID="com.lapsus.app.sandboxtest"
  IDENTITY="${LAPSUS_MAC_APP_IDENTITY:--}"
  BUILD_DIR="$ROOT/build/mas-test"
else
  APP_NAME="Lapsus"
  BUNDLE_ID="com.lapsus.app"
  BUILD_DIR="$ROOT/build/mas"
  for v in LAPSUS_MAS_APP_IDENTITY LAPSUS_MAS_INSTALLER_IDENTITY LAPSUS_MAS_PROFILE; do
    if [ -z "${!v:-}" ]; then
      echo "$v is not set (see the header of this script, or pass --test)." >&2
      exit 1
    fi
  done
  IDENTITY="$LAPSUS_MAS_APP_IDENTITY"
fi
# App Store Connect wants every upload's CFBundleVersion unique and at most three integers
# (ITMS-90257), and macOS compares it to decide what is newer. The build time is all of that
# without anyone having to remember the last number.
BUILD_NUMBER="${LAPSUS_MAS_BUILD:-$(date -u +%Y%m%d.%H%M)}"
if ! [[ "$BUILD_NUMBER" =~ ^[0-9]+(\.[0-9]+){0,2}$ ]]; then
  echo "LAPSUS_MAS_BUILD must be at most three numbers separated by dots, not $BUILD_NUMBER." >&2
  exit 1
fi

PUBLISH_DIR="$BUILD_DIR/publish"
if $TEST; then
  APP="$BUILD_DIR/$APP_NAME.app"
else
  # The store app is staged out of sight and removed once it is in the .pkg. Left in build/mas, Launch
  # Services knew com.lapsus.app there, and TestFlight installed its copy over it (owned by root, in
  # the repository) instead of in /Applications.
  APP="$BUILD_DIR/stage/$APP_NAME.app"
fi
ENTITLEMENTS="$BUILD_DIR/Lapsus.entitlements"

rm -rf "$PUBLISH_DIR" "$APP"
mkdir -p "$BUILD_DIR"

echo "==> Publishing ($RID, v$VERSION, build $BUILD_NUMBER)"
# Single-file, not the usual apphost. The sandbox grants Launch Services only to a main executable
# that links it (frameworks.sb keys on the executable's own load commands). The plain apphost links
# libSystem alone, so AppKit aborts in _RegisterApplication at launch; the single-file host links
# CoreServices. See https://github.com/AvaloniaUI/Avalonia/issues/6529. It also leaves only Mach-O
# files in Contents/MacOS, where a managed .dll would need its signature in extended attributes.
dotnet publish "$ROOT/Lapsus/Lapsus.csproj" -c Release -r "$RID" --self-contained \
  -p:PublishSingleFile=true -p:MacAppStore=true -o "$PUBLISH_DIR"

echo "==> Assembling $APP_NAME.app"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
ditto "$PUBLISH_DIR" "$APP/Contents/MacOS"
# Every executable in a store bundle must carry the sandbox entitlement; nothing launches the
# crash dumper, so it is left out rather than signed.
rm -f "$APP/Contents/MacOS/createdump"
find "$APP/Contents/MacOS" -name '*.pdb' -delete

# StoreKit 2 bridge (subscription and lifetime purchase). Beside the executable, where .NET looks
# for a native library.
"$ROOT/scripts/build-storekit-bridge.sh" "$APP/Contents/MacOS"

"$ROOT/scripts/make-macos-icon.sh" "$BUILD_DIR" "$APP/Contents/Resources/AppIcon.icns"

# LSMinimumSystemVersion 13: login items go through SMAppService. LSMultipleInstancesProhibited is
# the single-instance check (the sandbox denies the named mutex SingleInstance uses elsewhere).
cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleExecutable</key>
    <string>$EXECUTABLE</string>
    <key>CFBundleIdentifier</key>
    <string>$BUNDLE_ID</string>
    <key>CFBundleName</key>
    <string>$APP_NAME</string>
    <key>CFBundleDisplayName</key>
    <string>$APP_NAME</string>
    <key>CFBundleIconFile</key>
    <string>AppIcon</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleShortVersionString</key>
    <string>$VERSION</string>
    <key>CFBundleVersion</key>
    <string>$BUILD_NUMBER</string>
    <key>LSApplicationCategoryType</key>
    <string>public.app-category.productivity</string>
    <key>LSMinimumSystemVersion</key>
    <string>13.0</string>
    <key>LSMultipleInstancesProhibited</key>
    <true/>
    <key>LSUIElement</key>
    <true/>
    <key>ITSAppUsesNonExemptEncryption</key>
    <false/>
    <key>NSHighResolutionCapable</key>
    <true/>
    <key>NSMicrophoneUsageDescription</key>
    <string>Lapsus listens only while voice typing is on and you have pressed its hotkey, and turns your speech into text on this Mac.</string>
    <key>NSPrincipalClass</key>
    <string>NSApplication</string>
</dict>
</plist>
PLIST

cp "$ENTITLEMENTS_BASE" "$ENTITLEMENTS"
if ! $TEST; then
  # The store checks that the signature claims the same app and team as the profile it embeds.
  PROFILE_PLIST="$BUILD_DIR/profile.plist"
  security cms -D -i "$LAPSUS_MAS_PROFILE" > "$PROFILE_PLIST"
  TEAM_ID="$(/usr/libexec/PlistBuddy -c 'Print :TeamIdentifier:0' "$PROFILE_PLIST")"
  PROFILE_APP_ID="$(/usr/libexec/PlistBuddy -c 'Print :Entitlements:com.apple.application-identifier' "$PROFILE_PLIST")"
  if [ "$PROFILE_APP_ID" != "$TEAM_ID.$BUNDLE_ID" ]; then
    echo "The profile is for $PROFILE_APP_ID, not $TEAM_ID.$BUNDLE_ID." >&2
    exit 1
  fi
  /usr/libexec/PlistBuddy -c "Add :com.apple.application-identifier string $TEAM_ID.$BUNDLE_ID" "$ENTITLEMENTS"
  /usr/libexec/PlistBuddy -c "Add :com.apple.developer.team-identifier string $TEAM_ID" "$ENTITLEMENTS"
  cp "$LAPSUS_MAS_PROFILE" "$APP/Contents/embedded.provisionprofile"
fi

echo "==> Signing ($([ "$IDENTITY" = "-" ] && echo "ad-hoc" || echo "$IDENTITY"))"
# App Store Connect rejects a bundle with any file still carrying com.apple.quarantine (ITMS-91109).
# The profile picks it up from the browser that downloaded it, and cp copies extended attributes.
xattr -cr "$APP"
# Inside out: the native libraries first, the bundle last. Only the main executable carries the
# entitlements; libraries run in the sandbox of the process that loads them.
find "$APP/Contents/MacOS" -type f ! -path "$APP/Contents/MacOS/$EXECUTABLE" -print0 |
  xargs -0 codesign --force --timestamp=none --sign "$IDENTITY"
codesign --force --timestamp=none --sign "$IDENTITY" \
  --identifier "$BUNDLE_ID" --entitlements "$ENTITLEMENTS" "$APP"
codesign --verify --deep --strict "$APP"

if $TEST; then
  echo "==> Done: $APP"
  echo "    Quit any running Lapsus first, then: open \"$APP\""
  echo "    Logs: ~/Library/Containers/$BUNDLE_ID/Data/Library/Application Support/Lapsus/"
  exit 0
fi

PKG="$BUILD_DIR/$APP_NAME-$VERSION-$BUILD_NUMBER.pkg"
echo "==> Building $PKG"
productbuild --component "$APP" /Applications --sign "$LAPSUS_MAS_INSTALLER_IDENTITY" "$PKG"

/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister \
  -u "$APP" 2>/dev/null || true
rm -rf "$BUILD_DIR/stage"

echo "==> Done: $PKG"
echo "    Upload it with Transporter (or: xcrun altool --upload-package)."
