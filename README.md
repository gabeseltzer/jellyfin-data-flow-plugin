# Jellyfin Data Flow

A Jellyfin server plugin that adds a live network throughput graph to the web client's
**Playback Info** overlay: client download/upload for the current playback, server-wide
upload/download, a rolling average, and colour coding against the bitrate the video needs.

Status: planning. See [SPEC.md](SPEC.md), [PLAN.md](PLAN.md) and
[docs/RESEARCH.md](docs/RESEARCH.md).

Scope note: this targets the Jellyfin **web** client (and apps that embed it). Native
clients such as Android TV render their own stats overlay and cannot be extended by a
server plugin.
