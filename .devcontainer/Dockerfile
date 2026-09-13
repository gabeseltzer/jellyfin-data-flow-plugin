# 10.0-noble, not 1-10.0-noble: the `1-` variant is still pinned to the .NET 10 RC SDK
# (10.0.100-rc.2), so local builds ran preview analyzers while CI's setup-dotnet 10.0.x
# resolved GA. Same Ubuntu 24.04 base and same vscode uid; this one ships the GA SDK.
FROM mcr.microsoft.com/devcontainers/dotnet:10.0-noble

# ffmpeg: generate synthetic test media. jq/curl: poke the Jellyfin API from scripts.
# Some base image revisions carry a Yarn apt repo whose signing key has expired; we do not need it.
RUN rm -f /etc/apt/sources.list.d/yarn.list \
    && apt-get update \
    && apt-get install -y --no-install-recommends ffmpeg jq curl unzip \
    && apt-get clean \
    && rm -rf /var/lib/apt/lists/*
