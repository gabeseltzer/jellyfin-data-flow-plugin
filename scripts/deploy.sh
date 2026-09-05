#!/usr/bin/env bash
# Build the plugin, copy it into the Jellyfin container's plugin dir, restart Jellyfin,
# and confirm the plugin loaded via the API.
set -euo pipefail
cd "$(dirname "$0")/.."

PROJECT=Jellyfin.Plugin.DataFlow
CONTAINER=${JELLYFIN_CONTAINER:-jellyfin-dataflow-dev}
URL=${JELLYFIN_URL:-http://jellyfin:8096}
CONFIG=${CONFIGURATION:-Debug}

if [ ! -f "$PROJECT/$PROJECT.csproj" ]; then
  echo "No $PROJECT/$PROJECT.csproj yet. Nothing to deploy." >&2
  exit 1
fi

VERSION=$(grep -oP '(?<=<AssemblyVersion>)[^<]+' "$PROJECT/$PROJECT.csproj" || echo 0.0.0.1)
DEST="dist/plugins/DataFlow_${VERSION}"

echo "Building $PROJECT ($CONFIG)..."
dotnet build "$PROJECT/$PROJECT.csproj" -c "$CONFIG" --nologo -v quiet

echo "Copying to $DEST"
rm -rf dist/plugins/DataFlow_*
mkdir -p "$DEST"
cp "$PROJECT/bin/$CONFIG/net9.0/$PROJECT.dll" "$DEST/"
[ -f "$PROJECT/bin/$CONFIG/net9.0/$PROJECT.pdb" ] && cp "$PROJECT/bin/$CONFIG/net9.0/$PROJECT.pdb" "$DEST/"
[ -f meta.json ] && sed "s/__VERSION__/$VERSION/g" meta.json > "$DEST/meta.json"

echo "Restarting $CONTAINER..."
docker restart "$CONTAINER" >/dev/null

echo -n "Waiting for Jellyfin"
for _ in $(seq 1 60); do
  if curl -fsS "$URL/health" >/dev/null 2>&1; then echo " up."; break; fi
  echo -n "."; sleep 1
done

echo "Recent plugin log lines:"
docker logs --since 90s "$CONTAINER" 2>&1 | grep -i -E "dataflow|plugin" | tail -n 15 || true
