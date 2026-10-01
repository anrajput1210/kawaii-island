#!/bin/sh
# Builds "Kawaii Island.app" (universal: Apple silicon + Intel) and KawaiiIsland.dmg into macos/build.
set -e
cd "$(dirname "$0")/.."
swift build -c release --arch arm64 --arch x86_64
BIN="$(swift build -c release --arch arm64 --arch x86_64 --show-bin-path)/KawaiiIsland"
rm -rf build && mkdir -p "build/dmg/Kawaii Island.app/Contents/MacOS"
APP="build/dmg/Kawaii Island.app"
cp "$BIN" "$APP/Contents/MacOS/KawaiiIsland"
cp Info.plist "$APP/Contents/Info.plist"
codesign --force --deep -s - "$APP"   # ad-hoc; Developer ID signing + notarization later
ln -s /Applications build/dmg/Applications
hdiutil create -volname "Kawaii Island" -srcfolder build/dmg -ov -format UDZO build/KawaiiIsland.dmg
echo "built macos/build/KawaiiIsland.dmg"
