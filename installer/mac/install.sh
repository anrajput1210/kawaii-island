#!/bin/sh
# Kawaii Island for Mac: one-line install without the "Apple could not verify…" prompt.
#   curl -fsSL https://raw.githubusercontent.com/anrajput1210/kawaii-island/main/installer/mac/install.sh | sh
# Downloads the latest release zip with curl (curl downloads aren't quarantined, so Gatekeeper doesn't stop the
# not-yet-notarized app), unpacks it to ~/Applications, opens it at login and launches it. Nothing else is touched.
set -e
APP="$HOME/Applications/Kawaii Island.app"
URL="https://github.com/anrajput1210/kawaii-island/releases/latest/download/KawaiiIsland-macos.zip"
TMP="$(mktemp -d)"
echo "Downloading Kawaii Island…"
curl -fsSL "$URL" -o "$TMP/KawaiiIsland-macos.zip"
pkill -x KawaiiIsland 2>/dev/null || true
mkdir -p "$HOME/Applications"
rm -rf "$APP"
ditto -x -k "$TMP/KawaiiIsland-macos.zip" "$HOME/Applications"
rm -rf "$TMP"
xattr -dr com.apple.quarantine "$APP" 2>/dev/null || true
open "$APP" --args --enable-login
echo "Installed to $APP and started (Control+Option+I opens it). Uninstall: quit it from the menu bar, then delete the app."
