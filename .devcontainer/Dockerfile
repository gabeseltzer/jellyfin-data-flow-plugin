FROM mcr.microsoft.com/devcontainers/dotnet:1-9.0-bookworm

# ffmpeg: generate synthetic test media. jq/curl: poke the Jellyfin API from scripts.
# The base image carries a Yarn apt repo whose signing key has expired; we don't need it.
RUN rm -f /etc/apt/sources.list.d/yarn.list \
    && apt-get update \
    && apt-get install -y --no-install-recommends ffmpeg jq curl unzip \
    && apt-get clean \
    && rm -rf /var/lib/apt/lists/*
