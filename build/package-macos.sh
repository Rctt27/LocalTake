#!/usr/bin/env bash
# Builds LocalTake.app for one macOS architecture and zips it.
# Usage: build/package-macos.sh osx-arm64|osx-x64 OUTPUT_DIR
# Runs on macOS (sips, iconutil, codesign, ditto). The bundle is signed ad hoc: without an Apple Developer ID it is
# not notarized, and Gatekeeper asks to confirm the first launch (right-click > Open).
set -euo pipefail

rid=$1
mkdir -p "$2"
out=$(cd "$2" && pwd)
root=$(cd "$(dirname "$0")/.." && pwd)
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

version=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$root/Directory.Build.props")
app="$work/LocalTake.app"

dotnet publish "$root/src/LocalTake.App" -c Release -r "$rid" --self-contained true \
  -p:PublishSingleFile=false -p:UseAppHost=true -o "$work/publish"

mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
cp -R "$work/publish/." "$app/Contents/MacOS/"
sed "s/__VERSION__/$version/g" "$root/build/macos/Info.plist" > "$app/Contents/Info.plist"

# Icon: the .ico at every size of an iconset. A missing icon does not stop the build.
if sips -s format png "$root/src/LocalTake.App/Assets/LocalTake.ico" --out "$work/icon.png" >/dev/null 2>&1; then
  iconset="$work/LocalTake.iconset"
  mkdir -p "$iconset"
  for size in 16 32 128 256 512; do
    sips -z $size $size "$work/icon.png" --out "$iconset/icon_${size}x${size}.png" >/dev/null
    sips -z $((size * 2)) $((size * 2)) "$work/icon.png" --out "$iconset/icon_${size}x${size}@2x.png" >/dev/null
  done
  iconutil -c icns "$iconset" -o "$app/Contents/Resources/LocalTake.icns" || echo "warning: no icon"
else
  echo "warning: the icon could not be converted"
fi

codesign --force --deep --sign - "$app"
zip="$out/LocalTake-$version-macos-${rid#osx-}.zip"
ditto -c -k --keepParent "$app" "$zip"
echo "$zip"
