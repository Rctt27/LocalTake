#!/usr/bin/env bash
# Completes the built LocalTake.app and zips it, for the CI artifact and the release alike: adds the shipped ffmpeg and
# the licence notices, signs, checks both architectures, zips.
#
# Signing: with MACOS_SIGNING_P12 (a base64 .p12 holding the "LocalTake" self-signed code-signing certificate) and
# MACOS_SIGNING_PASSWORD, the app is signed with that certificate, which stays the same from one version to the next,
# so macOS keeps the Screen Recording and Microphone permissions it was given. Without them (a fork, an outside pull
# request), it is signed ad hoc. See docs/macos-signing.md.
#
# Usage: package-macos.sh <LocalTake.app> <zip>
set -euo pipefail

if [ $# -ne 2 ]; then
  echo "usage: $0 <LocalTake.app> <zip>" >&2
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
cp "$repo/THIRD-PARTY-NOTICES.md" "$app/Contents/Resources/"
cp -R "$repo/licenses" "$app/Contents/Resources/"

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
  identity=LocalTake
else
  echo "No signing certificate: signed ad hoc"
  identity=-
fi

# The nested executable first, then the app around it.
codesign --force --sign "$identity" "$app/Contents/MacOS/ffmpeg"
codesign --force --deep --sign "$identity" "$app"
codesign --verify --deep --strict --verbose=2 "$app"
if [ "$identity" != "-" ]; then
  details=$(codesign -dvv "$app" 2>&1)
  [[ "$details" == *"Authority=LocalTake"* ]] || fail "not signed by LocalTake: $details"
  echo "Signed with the LocalTake certificate"
fi

for binary in "$app/Contents/MacOS/LocalTake" "$app/Contents/MacOS/ffmpeg"; do
  archs=$(lipo -archs "$binary")
  echo "$binary: $archs"
  [[ "$archs" == *x86_64* && "$archs" == *arm64* ]] || fail "$binary is not universal"
done

mkdir -p "$(dirname "$zip")"
ditto -c -k --keepParent "$app" "$zip"
