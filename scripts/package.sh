#!/usr/bin/env bash
# Package the built plugin into artifacts/<name>_<version>.zip plus a manifest.json usable as a
# Jellyfin plugin repository (point the Dashboard > Plugins > Repositories at the raw manifest URL).
set -euo pipefail
cd "$(dirname "$0")/.."
CONFIG=${1:-Release}
PROJECT=Jellyfin.Plugin.DataFlow
VERSION=$(grep -oP '(?<=<AssemblyVersion>)[^<]+' "$PROJECT/$PROJECT.csproj")
ABI=$(grep -oP '(?<=targetAbi: ")[^"]+' build.yaml)
REPO_URL=${REPO_URL:-https://github.com/gabeseltzer/jellyfin-data-flow-plugin}
OUT=artifacts; ZIP="dataflow_${VERSION}.zip"
DLL="$PROJECT/bin/$CONFIG/net9.0/$PROJECT.dll"
[ -f "$DLL" ] || dotnet build "$PROJECT/$PROJECT.csproj" -c "$CONFIG" --nologo -v quiet

rm -rf "$OUT" && mkdir -p "$OUT/stage"
cp "$DLL" "$OUT/stage/"
sed "s/__VERSION__/$VERSION/g" meta.json > "$OUT/stage/meta.json"
(cd "$OUT/stage" && zip -q "../$ZIP" ./*)
rm -rf "$OUT/stage"
CHECKSUM=$(md5sum "$OUT/$ZIP" | cut -d' ' -f1)
TIMESTAMP=$(date -u +%Y-%m-%dT%H:%M:%SZ)
jq -n --arg v "$VERSION" --arg abi "$ABI" --arg sum "$CHECKSUM" --arg ts "$TIMESTAMP" \
  --arg url "$REPO_URL/releases/download/v${VERSION%.0}/$ZIP" '
[{
  guid: "b7e3d5a2-4c1f-4e8a-9b6d-2f0a7c9e1d3b",
  name: "Data Flow",
  description: "Live network throughput graph in the web client Playback Info overlay.",
  overview: "Live network throughput graph in Playback Info.",
  owner: "gabeseltzer",
  category: "General",
  imageUrl: "",
  versions: [{
    version: $v, changelog: "Initial release.", targetAbi: $abi, sourceUrl: $url, checksum: $sum, timestamp: $ts
  }]
}]' > "$OUT/manifest.json"
echo "Packaged $OUT/$ZIP ($CHECKSUM)"; ls -la "$OUT"
