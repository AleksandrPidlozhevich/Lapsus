#!/usr/bin/env bash
set -uo pipefail

# Removes every Lapsus from this Mac for a clean install: the website build, the App Store / TestFlight
# copy, local test builds (Lapsus Sandbox), their privacy grants, settings, dictionaries, logs and
# login items.
#
#   scripts/uninstall-macos.sh             list what goes, ask, then remove
#   scripts/uninstall-macos.sh -y          no question
#   scripts/uninstall-macos.sh --keep-data keep settings and dictionaries
#   scripts/uninstall-macos.sh --dry-run   only list

ASSUME_YES=false
KEEP_DATA=false
DRY_RUN=false
for arg in "$@"; do
  case "$arg" in
    -y|--yes) ASSUME_YES=true ;;
    --keep-data) KEEP_DATA=true ;;
    --dry-run) DRY_RUN=true ;;
    *) echo "Unknown option: $arg" >&2; exit 2 ;;
  esac
done

BUNDLE_IDS=(com.lapsus.app com.lapsus.app.sandboxtest)
LSREGISTER=/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister

bundle_id_of() {
  /usr/libexec/PlistBuddy -c 'Print :CFBundleIdentifier' "$1/Contents/Info.plist" 2>/dev/null
}

is_lapsus() {
  case "$(bundle_id_of "$1")" in
    com.lapsus.app|com.lapsus.app.*) return 0 ;;
    *) return 1 ;;
  esac
}

APPS=()
add_app() {
  local p="$1" existing
  [ -d "$p" ] && is_lapsus "$p" || return 0
  for existing in "${APPS[@]+"${APPS[@]}"}"; do
    [ "$existing" = "$p" ] && return 0
  done
  APPS+=("$p")
}

# Launch Services remembers copies that are long gone from Spotlight's index, and the Installer puts a
# new copy wherever it last saw one, so both are asked.
REGISTERED=()
while IFS= read -r p; do
  REGISTERED+=("$p")
  add_app "$p"
done < <("$LSREGISTER" -dump 2>/dev/null | sed -n 's/^path: *\(.*[Ll]apsus[^/]*\.app\) (0x[0-9a-f]*)$/\1/p' | sort -u)

for id in "${BUNDLE_IDS[@]}"; do
  while IFS= read -r p; do add_app "$p"; done < <(mdfind "kMDItemCFBundleIdentifier == '$id'" 2>/dev/null)
done
for p in /Applications/Lapsus*.app "$HOME/Applications/Lapsus"*.app "$HOME/.Trash/Lapsus"*.app; do
  add_app "$p"
done

DATA=()
if ! $KEEP_DATA; then
  for p in \
    "$HOME/Library/Application Support/Lapsus" \
    "$HOME/Library/Containers/com.lapsus.app" \
    "$HOME/Library/Containers/com.lapsus.app.sandboxtest" \
    "$HOME/Library/Caches/com.lapsus.app" \
    "$HOME/Library/HTTPStorages/com.lapsus.app" \
    "$HOME/Library/Preferences/com.lapsus.app.plist" \
    "$HOME/Library/Saved Application State/com.lapsus.app.savedState" \
    "$HOME/Library/Logs/velopack_Lapsus.log"; do
    [ -e "$p" ] && DATA+=("$p")
  done
fi
LAUNCH_AGENT="$HOME/Library/LaunchAgents/com.lapsus.app.plist"

echo "Apps:"
[ ${#APPS[@]} -eq 0 ] && echo "  (none)"
for p in "${APPS[@]+"${APPS[@]}"}"; do echo "  $p ($(bundle_id_of "$p"))"; done
echo "Data:"
if $KEEP_DATA; then echo "  (kept)"; elif [ ${#DATA[@]} -eq 0 ]; then echo "  (none)"; fi
for p in "${DATA[@]+"${DATA[@]}"}"; do echo "  $p"; done
[ -e "$LAUNCH_AGENT" ] && echo "Login item:" && echo "  $LAUNCH_AGENT"
echo "Privacy grants: ${BUNDLE_IDS[*]}"

$DRY_RUN && exit 0
if ! $ASSUME_YES; then
  read -r -p "Remove all of this? [y/N] " answer
  [[ "$answer" =~ ^[Yy] ]] || exit 1
fi

FAILED=()

for id in "${BUNDLE_IDS[@]}"; do
  osascript -e "tell application id \"$id\" to quit" >/dev/null 2>&1 || true
done
sleep 2
pkill -f '/Lapsus[^/]*\.app/Contents/MacOS/Lapsus' 2>/dev/null || true

# tccutil only finds an app Launch Services still knows, so the grants go before the apps do.
for id in "${BUNDLE_IDS[@]}"; do
  tccutil reset All "$id" >/dev/null 2>&1 || true
done

launchctl bootout "gui/$(id -u)/com.lapsus.app" >/dev/null 2>&1 || true
rm -f "$LAUNCH_AGENT"

# App Store and TestFlight installs belong to root.
remove() {
  local p="$1"
  rm -rf "$p" 2>/dev/null
  [ -e "$p" ] && sudo -n rm -rf "$p" 2>/dev/null
  [ -e "$p" ] && [ -t 0 ] && sudo rm -rf "$p"
  # Without a terminal for sudo, Finder can still move an app to the Trash.
  if [ -e "$p" ] && [ -f "$p/Contents/Info.plist" ] &&
     osascript -e "tell application \"Finder\" to delete (POSIX file \"$p\" as alias)" >/dev/null 2>&1; then
    TRASHED+=("$p")
    "$LSREGISTER" -u "$HOME/.Trash/$(basename "$p")" >/dev/null 2>&1
  fi
  [ -e "$p" ] && FAILED+=("$p")
}
TRASHED=()

for p in "${APPS[@]+"${APPS[@]}"}"; do
  "$LSREGISTER" -u "$p" >/dev/null 2>&1
  remove "$p"
done
for p in "${REGISTERED[@]+"${REGISTERED[@]}"}"; do
  "$LSREGISTER" -u "$p" >/dev/null 2>&1
done
# A container keeps a metadata file nobody may delete, not even root; once everything else is gone,
# containermanagerd removes the folder itself.
remove_container() {
  local p="$1" left
  rm -rf "$p" 2>/dev/null
  [ -e "$p" ] || return 0
  left="$(find "$p" -mindepth 1 ! -name '.com.apple.containermanagerd.metadata.plist' 2>/dev/null | head -1)"
  [ -z "$left" ] || FAILED+=("$p")
}

for p in "${DATA[@]+"${DATA[@]}"}"; do
  case "$p" in
    "$HOME/Library/Containers/"*) remove_container "$p" ;;
    *) remove "$p" ;;
  esac
done
"$LSREGISTER" -gc >/dev/null 2>&1

echo
for p in "${TRASHED[@]+"${TRASHED[@]}"}"; do
  echo "Moved to the Trash (empty it to finish): $p"
done
LEFT="$("$LSREGISTER" -dump 2>/dev/null | grep -E '^path:.*[Ll]apsus[^/]*\.app' | grep -v '/Volumes/' || true)"
if [ ${#FAILED[@]} -eq 0 ] && [ -z "$LEFT" ]; then
  echo "Done: no Lapsus left on this Mac."
  exit 0
fi

for p in "${FAILED[@]+"${FAILED[@]}"}"; do
  echo "Not removed: $p"
done
[ -n "$LEFT" ] && echo "Still registered:" && echo "$LEFT"
if printf '%s\n' "${FAILED[@]+"${FAILED[@]}"}" | grep -q '/Library/Containers/'; then
  echo "macOS guards app containers: give Terminal Full Disk Access (System Settings → Privacy &"
  echo "Security), or drag the folder to the Trash in Finder (⌘⇧G, ~/Library/Containers)."
fi
if printf '%s\n' "${FAILED[@]+"${FAILED[@]}"}" | grep -qv '/Library/Containers/'; then
  echo "Run the script again from Terminal so sudo can ask for the password."
fi
exit 1
