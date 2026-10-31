#!/usr/bin/env bash
# Fetches the Whisper model that ShowTime ships, at a pinned revision checked by SHA-256. ShowTime itself never
# downloads anything: this runs in CI, when the Windows zip and the macOS app are built.
#
# Usage: fetch-whisper-model.sh <dest-dir>   writes <dest-dir>/ggml-small-q5_1.bin
set -euo pipefail

# The ggerganov/whisper.cpp revision the model is downloaded from.
WHISPER_MODELS_REVISION=5359861c739e955e79d9a303bcbc70fb988958b1
WHISPER_MODEL=ggml-small-q5_1.bin
WHISPER_MODEL_SHA256=ae85e4a935d7a567bd102fe55afc16bb595bdb618e11b2fc7591bc08120411bb

if [ $# -ne 1 ]; then
  echo "usage: $0 <dest-dir>" >&2
  exit 2
fi
dest=$1
mkdir -p "$dest"
cd "$dest"
curl -sSfL --retry 3 -o "$WHISPER_MODEL" "https://huggingface.co/ggerganov/whisper.cpp/resolve/$WHISPER_MODELS_REVISION/$WHISPER_MODEL"
echo "$WHISPER_MODEL_SHA256  $WHISPER_MODEL" > "$WHISPER_MODEL.sha256"
if command -v sha256sum > /dev/null; then sha256sum -c "$WHISPER_MODEL.sha256"; else shasum -a 256 -c "$WHISPER_MODEL.sha256"; fi
rm "$WHISPER_MODEL.sha256"
