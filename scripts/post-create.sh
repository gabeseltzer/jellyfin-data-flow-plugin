#!/usr/bin/env bash
# Runs once after the devcontainer is created.
set -euo pipefail
cd "$(dirname "$0")/.."

mkdir -p dist/plugins media .dev/jellyfin/config .dev/jellyfin/cache

# The shared NuGet named volume is created root-owned on first mount; hand it to the dev user.
mkdir -p "$HOME/.nuget"
if [ ! -w "$HOME/.nuget/packages" ]; then
  sudo chown -R "$(id -u):$(id -g)" "$HOME/.nuget"
fi

# The ~/.claude bind mount is easy to get wrong: if the host path did not exist
# when Compose started, Docker creates an empty root-owned directory instead and
# Claude Code cannot log in. Say so plainly rather than letting it fail later.
if [ ! -w "$HOME/.claude" ]; then
  echo
  echo "WARNING: $HOME/.claude is not writable by $(id -un) -- Claude Code will not be able to log in."
  echo "  The host path for the mount probably does not exist, so Docker created an empty"
  echo "  root-owned placeholder. If VS Code runs Dev Containers inside WSL, set"
  echo "  CLAUDE_HOST_HOME in .devcontainer/.env (see .env.example), then rebuild."
  echo
fi

# The claude-code feature may install as root; `claude update` then fails with
# insufficient permissions. Hand the package dir to the dev user.
claude_pkg="$(npm root -g 2>/dev/null)/@anthropic-ai"
if [ -d "$claude_pkg" ] && [ ! -w "$claude_pkg" ]; then
  sudo chown -R "$(id -u):$(id -g)" "$claude_pkg"
fi

echo "dotnet SDKs:"
dotnet --list-sdks

if [ ! -f media/.generated ]; then
  echo "Generating synthetic test media (one-time)..."
  bash scripts/make-test-media.sh
fi

echo
echo "Jellyfin: http://localhost:8096 (first run: complete the setup wizard, add /media as a Movies library)"
echo "Deploy plugin: scripts/deploy.sh"
