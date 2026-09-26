#!/usr/bin/env bash
# Builds "Bring Later.app" (universal, ad-hoc signed) and a zip of it in mac/build.
# VERSION=1.2.3 mac/scripts/bundle.sh
set -euo pipefail

cd "$(dirname "$0")/.."
version="${VERSION:-0.2.0}"
brand="../assets/brand/generated"
app="build/Bring Later.app"

swift build -c release --arch arm64 --arch x86_64 --product BringLater
binary="$(swift build -c release --arch arm64 --arch x86_64 --show-bin-path)/BringLater"

rm -rf build
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
cp "$binary" "$app/Contents/MacOS/BringLater"
cp "$brand/AppIcon.icns" "$brand/MenuBarTemplate.png" "$brand/MenuBarTemplate@2x.png" "$app/Contents/Resources/"
cp -R Localization/*.lproj "$app/Contents/Resources/"
sed "s/__VERSION__/$version/g" Info.plist > "$app/Contents/Info.plist"

# Ad-hoc signature: Apple silicon won't run unsigned code, and this needs no certificate.
codesign --force --sign - --timestamp=none "$app"

(cd build && ditto -c -k --keepParent "Bring Later.app" BringLater-mac.zip)
echo "Built $app ($version)"
