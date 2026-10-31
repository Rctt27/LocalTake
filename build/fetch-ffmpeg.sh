#!/usr/bin/env bash
# Fetches the ffmpeg that ShowTime ships, at a pinned version checked by SHA-256, and checks that it can do what
# ShowTime asks of it. ShowTime itself never downloads anything: this runs in CI, when the zips are built and before
# the tests, which run against the same binary.
#
# Usage: fetch-ffmpeg.sh windows|macos <dest-dir>
#   windows: <dest-dir>/ffmpeg.exe (gyan.dev's essentials build, the one "winget install Gyan.FFmpeg" installs)
#   macos:   <dest-dir>/ffmpeg, universal (Martin Riedl's arm64 and amd64 builds, which need macOS 12)
set -euo pipefail

FFMPEG_VERSION=9.0.2
WINDOWS_URL=https://github.com/GyanD/codexffmpeg/releases/download/9.0.2/ffmpeg-9.0.2-essentials_build.zip
WINDOWS_SHA256=60f467265b1e312373dbcd92200c2618a74850f98d3d078e94296bb3fa2047ba
MACOS_ARM64_URL=https://ffmpeg.martin-riedl.de/download/macos/arm64/1789931890_9.0.2/ffmpeg.zip
MACOS_ARM64_SHA256=c8ed4c4e6978a03c485edbfe4e0a5dc2380f8a30bba5150531b31b094492d924
MACOS_X64_URL=https://ffmpeg.martin-riedl.de/download/macos/amd64/1789931006_9.0.2/ffmpeg.zip
MACOS_X64_SHA256=7c6b4125b191cbf773832dc51f424cf2b6bb7da43007d1e066f95909e47cacd4

if [ $# -ne 2 ]; then
  echo "usage: $0 windows|macos <dest-dir>" >&2
  exit 2
fi
platform=$1
dest=$2
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
mkdir -p "$dest"

fail() {
  echo "fetch-ffmpeg: $*" >&2
  exit 1
}

# Downloads <url> to <file> and stops unless its SHA-256 is <sha256>. curl sends a User-Agent, which the macOS
# builds' server requires.
fetch() {
  curl -sSfL --retry 3 -o "$3" "$1"
  echo "$2  $3" > "$3.sha256"
  if command -v sha256sum > /dev/null; then sha256sum -c "$3.sha256"; else shasum -a 256 -c "$3.sha256"; fi
}

case "$platform" in
  windows)
    fetch "$WINDOWS_URL" "$WINDOWS_SHA256" "$work/ffmpeg.zip"
    unzip -q -o -j "$work/ffmpeg.zip" '*/bin/ffmpeg.exe' -d "$dest"
    ffmpeg="$dest/ffmpeg.exe"
    devices=(gdigrab dshow)
    ;;
  macos)
    fetch "$MACOS_ARM64_URL" "$MACOS_ARM64_SHA256" "$work/arm64.zip"
    fetch "$MACOS_X64_URL" "$MACOS_X64_SHA256" "$work/x86_64.zip"
    unzip -q -o "$work/arm64.zip" ffmpeg -d "$work/arm64"
    unzip -q -o "$work/x86_64.zip" ffmpeg -d "$work/x86_64"
    lipo -create -output "$dest/ffmpeg" "$work/arm64/ffmpeg" "$work/x86_64/ffmpeg"
    chmod +x "$dest/ffmpeg"
    archs=$(lipo -archs "$dest/ffmpeg")
    [[ "$archs" == *arm64* && "$archs" == *x86_64* ]] || fail "not universal: $archs"
    ffmpeg="$dest/ffmpeg"
    devices=(avfoundation)
    ;;
  *)
    echo "usage: $0 windows|macos <dest-dir>" >&2
    exit 2
    ;;
esac

# Whole outputs in variables: a pipe into head or grep -q could end ffmpeg with SIGPIPE, which pipefail reports.
version=$("$ffmpeg" -hide_banner -version)
version=${version%%$'\n'*}
[[ "$version" == "ffmpeg version $FFMPEG_VERSION"* ]] || fail "unexpected version: $version"
buildconf=$("$ffmpeg" -hide_banner -buildconf)
[[ "$buildconf" == *--enable-libx264* ]] || fail "built without libx264"
device_list=$("$ffmpeg" -hide_banner -devices)
for device in "${devices[@]}"; do
  grep -qw "$device" <<< "$device_list" || fail "no $device input"
done
echo "$version: $ffmpeg"
