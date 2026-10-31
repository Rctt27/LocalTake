#!/usr/bin/env bash
# Completes the built ShowTime.app and zips it, for the CI artifact and the release alike: adds the shipped ffmpeg, the
# Whisper model, whisper.cpp and the licence notices, signs, checks both architectures, zips.
#
# Signing: with MACOS_SIGNING_P12 (a base64 .p12 holding the "LocalTake" self-signed code-signing certificate) and
# MACOS_SIGNING_PASSWORD, the app is signed with that certificate, which stays the same from one version to the next,
# so macOS keeps the Screen Recording and Microphone permissions it was given. Without them (a fork, an outside pull
# request), it is signed ad hoc, unless REQUIRE_SIGNING=1 (the release), which then stops: an ad hoc release would
# make every user grant the permissions again. See docs/macos-signing.md.
#
# Usage: package-macos.sh <ShowTime.app> <zip>
set -euo pipefail

if [ $# -ne 2 ]; then
  echo "usage: $0 <ShowTime.app> <zip>" >&2
  exit 2
fi
app=$1
zip=$2
repo=$(cd "$(dirname "$0")/.." && pwd)
work=$(mktemp -d)
keychain="$work/signing.keychain-db"

cleanup() {
  if [ -f "$keychain" ]; then
    security delete-keychain "$keychain" || true
  fi
  rm -rf "$work"
}
trap cleanup EXIT

fail() {
  echo "package-macos: $*" >&2
  exit 1
}

bash "$repo/build/fetch-ffmpeg.sh" macos "$work/ffmpeg"
cp "$work/ffmpeg/ffmpeg" "$app/Contents/MacOS/ffmpeg"
# The transcription model, where AppPaths looks first; ShowTime itself never downloads it.
bash "$repo/build/fetch-whisper-model.sh" "$app/Contents/Resources/models"
cp "$repo/THIRD-PARTY-NOTICES.md" "$app/Contents/Resources/"
cp -R "$repo/licenses" "$app/Contents/Resources/"

# whisper.cpp, from the restored Whisper.net.Runtime package, where Whisper.net looks for it: runtimes/macos-<arch> next
# to the assemblies. Copied as they are, not through the build: the libraries find each other by their install names
# once Whisper.net has loaded them in order, as in a plain .NET program.
version=$(sed -nE 's/.*Include="Whisper\.net\.Runtime" Version="([^"]+)".*/\1/p' "$repo/Directory.Packages.props")
packages=$(dotnet nuget locals global-packages --list | sed 's/^global-packages: //')
whisper="$app/Contents/MonoBundle/runtimes"
for arch in arm64 x64; do
  source="$packages/whisper.net.runtime/$version/build/macos-$arch"
  [ -f "$source/libwhisper.dylib" ] || fail "no $source/libwhisper.dylib: restore the projects first"
  mkdir -p "$whisper/macos-$arch"
  cp "$source"/*.dylib "$whisper/macos-$arch/"
done

if [ -n "${MACOS_SIGNING_P12:-}" ]; then
  password=$(uuidgen)
  echo "$MACOS_SIGNING_P12" | base64 --decode > "$work/signing.p12"
  security create-keychain -p "$password" "$keychain"
  security set-keychain-settings -lut 21600 "$keychain"
  security unlock-keychain -p "$password" "$keychain"
  security import "$work/signing.p12" -k "$keychain" -P "${MACOS_SIGNING_PASSWORD:-}" -T /usr/bin/codesign
  security set-key-partition-list -S apple-tool:,apple:,codesign: -s -k "$password" "$keychain" > /dev/null
  # shellcheck disable=SC2046 # the existing keychains, one per line
  security list-keychains -d user -s "$keychain" $(security list-keychains -d user | tr -d '"')
  # The certificate keeps the app's former name (docs/macos-signing.md).
  identity=LocalTake
else
  if [ "${REQUIRE_SIGNING:-}" = 1 ]; then
    fail "MACOS_SIGNING_P12 is missing: a release is signed with the LocalTake certificate (docs/macos-signing.md)"
  fi
  # Shown in the run's summary too.
  echo "::warning::No signing certificate: signed ad hoc"
  identity=-
fi

# The nested code first, then the app around it.
codesign --force --sign "$identity" "$app/Contents/MacOS/ffmpeg"
find "$whisper" -name '*.dylib' -exec codesign --force --sign "$identity" {} \;
codesign --force --deep --sign "$identity" "$app"
codesign --verify --deep --strict --verbose=2 "$app"
if [ "$identity" != "-" ]; then
  details=$(codesign -dvv "$app" 2>&1)
  [[ "$details" == *"Authority=LocalTake"* ]] || fail "not signed by LocalTake: $details"
  echo "Signed with the LocalTake certificate"
fi

for binary in "$app/Contents/MacOS/ShowTime" "$app/Contents/MacOS/ffmpeg"; do
  archs=$(lipo -archs "$binary")
  echo "$binary: $archs"
  [[ "$archs" == *x86_64* && "$archs" == *arm64* ]] || fail "$binary is not universal"
done

mkdir -p "$(dirname "$zip")"
ditto -c -k --keepParent "$app" "$zip"
