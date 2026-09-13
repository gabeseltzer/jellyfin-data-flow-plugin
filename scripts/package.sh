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
DLL="$PROJECT/bin/$CONFIG/net10.0/$PROJECT.dll"
[ -f "$DLL" ] || dotnet build "$PROJECT/$PROJECT.csproj" -c "$CONFIG" --nologo -v quiet

rm -rf "$OUT" && mkdir -p "$OUT/stage"
cp "$DLL" "$OUT/stage/"
sed "s/__VERSION__/$VERSION/g" meta.json > "$OUT/stage/meta.json"
(cd "$OUT/stage" && zip -q "../$ZIP" ./*)
rm -rf "$OUT/stage"
CHECKSUM=$(md5sum "$OUT/$ZIP" | cut -d' ' -f1)
TIMESTAMP=$(date -u +%Y-%m-%dT%H:%M:%SZ)
CHANGELOG=$(jq -r .changelog meta.json)

# Keep every previously published version in the manifest. Each entry carries its own
# targetAbi, so a 10.11 server keeps being offered the last 10.11-compatible build while a
# 12.0 server is offered this one. Dropping them would strand older servers.
if [ -f manifest.json ]; then cp manifest.json "$OUT/previous.json"; else echo '[]' > "$OUT/previous.json"; fi

jq --arg v "$VERSION" --arg abi "$ABI" --arg sum "$CHECKSUM" --arg ts "$TIMESTAMP" \
  --arg log "$CHANGELOG" --arg url "$REPO_URL/releases/download/v${VERSION%.0}/$ZIP" '
(.[0].versions // []) as $old
| [{
  guid: "b7e3d5a2-4c1f-4e8a-9b6d-2f0a7c9e1d3b",
  name: "Data Flow",
  description: "Live network throughput graph in the web client Playback Info overlay.",
  overview: "Live network throughput graph in Playback Info.",
  owner: "gabeseltzer",
  category: "General",
  imageUrl: "",
  versions: ([{
    version: $v, changelog: $log, targetAbi: $abi, sourceUrl: $url, checksum: $sum, timestamp: $ts
  }] + [$old[] | select(.version != $v)])
}]' "$OUT/previous.json" > "$OUT/manifest.json"
rm -f "$OUT/previous.json"
echo "Packaged $OUT/$ZIP ($CHECKSUM)"; ls -la "$OUT"
