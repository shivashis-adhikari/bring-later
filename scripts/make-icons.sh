#!/usr/bin/env bash
# Regenerates every raster icon from the SVGs in assets/brand. macOS only: needs Google Chrome
# (headless, for accurate SVG rendering), ImageMagick and iconutil. Output is committed, so
# nobody else has to run this unless the logo changes.
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
brand="$root/assets/brand"
out="$brand/generated"
chrome="/Applications/Google Chrome.app/Contents/MacOS/Google Chrome"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

# render <svg> <size> <png>: draws the SVG into a transparent size×size square.
render() {
  local svg="$1" size="$2" png="$3"
  cat > "$work/page.html" <<HTML
<!doctype html><html><head><style>
html,body{margin:0;background:transparent;width:${size}px;height:${size}px;overflow:hidden}
img{display:block;width:${size}px;height:${size}px;object-fit:contain}
</style></head><body><img src="file://$svg"></body></html>
HTML
  "$chrome" --headless=new --disable-gpu --hide-scrollbars --force-device-scale-factor=1 \
    --default-background-color=00000000 --window-size="$size,$size" \
    --screenshot="$png" "file://$work/page.html" >/dev/null 2>&1
}

mkdir -p "$out"

# Windows: app icon (full mark from 48 px up, simplified mark below) and tray icon.
for size in 16 20 24 32 40; do render "$brand/src/mark-small.svg" "$size" "$work/win-$size.png"; done
for size in 48 64 96 128 256; do render "$brand/mark.svg" "$size" "$work/win-$size.png"; done
magick "$work"/win-{16,20,24,32,40,48,64,96,128,256}.png "$out/BringLater.ico"
for size in 16 20 24 32 40 48 64; do render "$brand/src/mark-small.svg" "$size" "$work/tray-$size.png"; done
magick "$work"/tray-{16,20,24,32,40,48,64}.png "$out/tray.ico"
render "$brand/mark.svg" 256 "$out/mark-256.png"

# macOS: app icon set and the menu bar template image.
iconset="$work/AppIcon.iconset"
mkdir -p "$iconset"
render "$brand/src/app-icon-mac.svg" 1024 "$work/mac-1024.png"
for size in 16 32 128 256 512; do
  magick "$work/mac-1024.png" -resize "${size}x${size}" "$iconset/icon_${size}x${size}.png"
  magick "$work/mac-1024.png" -resize "$((size * 2))x$((size * 2))" "$iconset/icon_${size}x${size}@2x.png"
done
iconutil -c icns "$iconset" -o "$out/AppIcon.icns"
cp "$work/mac-1024.png" "$out/app-icon-mac-1024.png"
render "$brand/src/menubar-template.svg" 18 "$out/MenuBarTemplate.png"
render "$brand/src/menubar-template.svg" 36 "$out/MenuBarTemplate@2x.png"

# Site: favicons.
render "$brand/mark.svg" 32 "$out/favicon-32.png"
render "$brand/src/touch-icon.svg" 180 "$out/apple-touch-icon.png"

# Windows Store package: tiles with the mark centered on a transparent background, and plain
# taskbar sizes ("unplated" means no tile behind them).
store="$out/store"
mkdir -p "$store"
render "$brand/mark.svg" 1024 "$work/mark-1024.png"
tile() { # tile <name> <width> <height> <mark size>
  magick "$work/mark-1024.png" -resize "${4}x${4}" -background none -gravity center -extent "${2}x${3}" "$store/$1"
}
for scale in 100 200; do
  f=$((scale / 100))
  tile "Square150x150Logo.scale-$scale.png" $((150 * f)) $((150 * f)) $((100 * f))
  tile "Wide310x150Logo.scale-$scale.png" $((310 * f)) $((150 * f)) $((100 * f))
  tile "Square44x44Logo.scale-$scale.png" $((44 * f)) $((44 * f)) $((44 * f))
  tile "StoreLogo.scale-$scale.png" $((50 * f)) $((50 * f)) $((50 * f))
done
for size in 16 24 32; do render "$brand/src/mark-small.svg" "$size" "$store/Square44x44Logo.targetsize-${size}.png"; done
for size in 48 256; do tile "Square44x44Logo.targetsize-${size}.png" "$size" "$size" "$size"; done
for size in 16 24 32 48 256; do
  cp "$store/Square44x44Logo.targetsize-${size}.png" "$store/Square44x44Logo.targetsize-${size}_altform-unplated.png"
done

echo "Icons written to $out"
