#!/usr/bin/env bash
# Runs once after the devcontainer is created.
set -euo pipefail
cd "$(dirname "$0")/.."

mkdir -p dist/plugins media .dev/jellyfin/config .dev/jellyfin/cache

echo "dotnet SDKs:"
dotnet --list-sdks

if [ ! -f media/.generated ]; then
  echo "Generating synthetic test media (one-time)..."
  bash scripts/make-test-media.sh
fi

echo
echo "Jellyfin: http://localhost:8096 (first run: complete the setup wizard, add /media as a Movies library)"
echo "Deploy plugin: scripts/deploy.sh"
