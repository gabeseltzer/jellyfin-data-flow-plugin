#!/usr/bin/env bash
# Generates small synthetic videos with known bitrates so the threshold colours are easy to verify.
# Output goes to media/ (git-ignored). Requires ffmpeg.
set -euo pipefail
cd "$(dirname "$0")/.."
mkdir -p media
DUR=${DUR:-120}

gen() { # name bitrate_kbps resolution
  local name=$1 kbps=$2 res=$3
  local out="media/${name} (2026).mp4"
  [ -f "$out" ] && return 0
  echo "  $out @ ${kbps}kbps ${res}"
  ffmpeg -loglevel error -y \
    -f lavfi -i "testsrc2=size=${res}:rate=30" \
    -f lavfi -i "sine=frequency=440:sample_rate=48000" \
    -t "$DUR" \
    -c:v libx264 -preset veryfast -b:v "${kbps}k" -minrate "${kbps}k" -maxrate "${kbps}k" -bufsize "$((kbps*2))k" \
    -pix_fmt yuv420p -g 60 \
    -c:a aac -b:a 128k -ac 2 \
    -movflags +faststart \
    "$out"
}

gen "Low Bitrate Test"   1500  1280x720
gen "Mid Bitrate Test"   6000  1920x1080
gen "High Bitrate Test" 20000  1920x1080
# Longer clip for throttling tests that need several minutes of playback.
DUR=480 gen "Long Mid Bitrate Test" 6000 1920x1080

touch media/.generated
echo "Done."
