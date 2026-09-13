# Jellyfin Data Flow Plugin — Specification

Status: draft v0.1 (2026-09-05)

## 1. Goal

Add a live network throughput graph to the Jellyfin web client's **Playback Info** overlay
(the stats panel toggled from the video OSD, implemented by jellyfin-web's `playerstats`
component). The graph shows how much data is flowing for the current playback, whether that
rate is enough to sustain the video, and what the server's overall network load looks like.

## 2. Scope

### In scope

- Jellyfin **web client** (jellyfin-web) served by the Jellyfin server, including the
  desktop app and any client that embeds jellyfin-web (e.g. Jellyfin Media Player).
- Jellyfin server **12.0.x** (.NET 10) as the primary target, built as plugin 0.2.x.
  **10.11.x** (.NET 9) stays supported through the already-published 0.1.x build; the two
  ABIs cannot share a single DLL.
- Video and audio playback via direct play, direct stream, remux, and transcode (HLS and
  progressive).

### Out of scope (v1)

- Native clients (Android, Android TV, iOS, Roku, Kodi, Swiftfin). They render their own
  stats overlays and cannot load injected JavaScript.
- Historical reporting / persistence. All data is in-memory and discarded on restart.
- Measuring "internet speed" with synthetic tests (speedtest-style downloads). We measure
  the **actual** data flow of playback, not link capacity. A one-shot probe may be added
  later as an optional feature.

## 3. User-facing behaviour

### 3.1 Placement

A new section titled **Data Flow** appears at the bottom of the Playback Info overlay,
below the existing categories (Playback Info, Transcoding Info, Original Media Info, ...).
It uses the same font, colours, and spacing as the surrounding rows so it looks native.

### 3.2 Contents

1. **Graph** (canvas), full width of the overlay, ~7em tall by default.
   - X axis: time, most recent sample on the right. Default window 2 minutes; the user
     can cycle 1 min / 2 min / 5 min by clicking the window label.
   - Y axis: throughput in kbps / Mbps, auto-scaled, with 2-3 gridlines and labels.
   - Series (each can be toggled by clicking its legend entry):
     - **Client download** (solid, primary). Bytes the client receives for this
       playback per second.
     - **Client upload** (thin). Bytes the client sends for this playback (requests,
       progress reports). Normally tiny; present because it is nearly free to include.
     - **Server upload** (dashed). Total bytes the server sends on its network
       interface(s) per second, all sessions included.
     - **Server download** (dashed, thin). Total bytes the server receives.
   - **Required bitrate line**: horizontal reference at the bitrate needed to sustain
     playback (see 3.4).
   - **Average line**: horizontal dashed line at the rolling mean of client download
     over the visible window.
   - **Colour coding**: the client-download area under the curve is filled green when
     the sample is at or above the required bitrate, amber when it is within the
     configurable margin (default 20 %) below it, and red when below that. The current
     value badge uses the same colour.
2. **Legend / current values row**: latest value for each series, the average, the
   required bitrate, and the buffer health (seconds buffered ahead) if available.
3. **Hover tooltip**: moving the mouse (or touching) over the graph shows a vertical
   cursor line and a tooltip with the timestamp and every series' value at that sample,
   plus the status (OK / marginal / starved).

### 3.3 Sizing

- The section stretches to the overlay's width; the canvas re-renders on
  `ResizeObserver` callbacks and on `devicePixelRatio` changes so it stays crisp.
- On TV layout (`playerStats-tv`) the graph height shrinks and hover is disabled.
- The overlay caps at `max-width: 50em` (set by jellyfin-web); we respect that.

### 3.4 Required bitrate

Priority order:

1. `session.TranscodingInfo.Bitrate` when the session is transcoding (this is the output
   bitrate the client must receive).
2. `mediaSource.Bitrate` for direct play / direct stream.
3. Sum of selected video + audio stream `BitRate` values.
4. If none available, the line is hidden and colour coding is disabled (grey fill).

HLS is bursty by nature: segments download faster than real time and then the connection
idles. To avoid false "starved" colours during idle gaps, the colour decision compares the
**5-second trailing mean** of client download against the required bitrate, while the
plotted line shows the raw 1 s buckets. Both values are in the tooltip. Buffer health
(seconds buffered ahead of the playhead) is shown alongside so a user can tell "bursty but
fine" from "actually falling behind".

### 3.5 Behaviour when hidden

When the Playback Info overlay is closed or no playback is active, the client code does
**no** polling, no timers, no rendering. Everything is driven by the overlay's visibility.

## 4. Data sources and measurement

| Series | Where measured | How |
|---|---|---|
| Client download | Server | ASP.NET middleware wraps the response body stream for media routes and counts bytes written, keyed by the requesting `deviceId` (and `playSessionId` when present). |
| Client download (fallback / cross-check) | Browser | `hls.js` `FRAG_LOADED` stats (bytes + duration) when the player uses HLS; Resource Timing entries (`transferSize`) for fetch/XHR media requests. Used when the server series is unavailable (e.g. media served by a reverse-proxy cache) and to display buffer health. |
| Client upload | Server | Same middleware counts request body bytes plus an estimate of header bytes for requests from that `deviceId`. |
| Server upload/download | Server | Background service samples `NetworkInterface.GetIPStatistics().BytesSent/BytesReceived` once per second across up, non-loopback interfaces (or a configured subset). |

Media routes counted (case-insensitive, respecting the server `BaseUrl`):

- `/Videos/{id}/stream`, `/Videos/{id}/stream.{ext}`
- `/Videos/{id}/hls1/{playlist}/{segment}.{ext}`, `/Videos/{id}/master.m3u8`, `/Videos/{id}/main.m3u8`
- `/Audio/{id}/stream`, `/Audio/{id}/stream.{ext}`, `/Audio/{id}/hls1/...`, `/Audio/{id}/master.m3u8`, `/Audio/{id}/main.m3u8`
- `/Videos/{id}/{mediaSourceId}/Subtitles/...` (small, but part of the playback flow)

Everything else (images, API JSON) is **not** counted toward client download so the graph
reflects media only. Client upload counts only requests carrying a `deviceId` query
parameter or an `Authorization` / `X-Emby-Authorization` header with a matching `DeviceId`.

Sampling: bytes are accumulated in a per-key `long` via `Interlocked.Add` and drained into
1-second buckets by the same timer that samples the NIC. Each key holds a ring buffer of
`HistorySeconds` (default 300) samples. Keys idle for 10 minutes are evicted.

## 5. API (server to web client)

All routes are under `/DataFlow` and return JSON unless noted.

| Method | Route | Auth | Purpose |
|---|---|---|---|
| GET | `/DataFlow/client.js` | anonymous | The injected client script (static, cache 1 h, ETag). Contains no secrets. |
| GET | `/DataFlow/client.css` | anonymous | Styles for the panel. |
| GET | `/DataFlow/Samples?deviceId=&since=` | authorized | Samples newer than `since` (unix ms) for that device plus server NIC samples over the same range, and the server's `now`. Capped at `HistorySeconds` entries. |
| GET | `/DataFlow/Config` | authorized | Client-relevant settings (poll interval, margin, window options). |

`Samples` response uses compact arrays to keep payloads a few hundred bytes per poll:

```json
{
  "now": 1757100000000,
  "interval": 1000,
  "t0": 1757099880000,
  "clientDown": [812345, 0, 0, 1234567],
  "clientUp":   [210, 0, 190, 0],
  "serverUp":   [2345678, 2100000, 1900000, 2400000],
  "serverDown": [12345, 11000, 9000, 14000]
}
```

Values are bytes per bucket; the client converts to bits per second.

## 6. Web injection

The plugin registers an `IStartupFilter` that adds middleware which rewrites
`/web/index.html` (and `/web/`, `/web`) responses to append
`<script src="{BaseUrl}/DataFlow/client.js" defer></script>` before `</body>`.
This is the technique used by the Jellyfin JavaScript Injector plugin and works on 10.11
and 12 without touching files on disk. It handles compression (strips `Accept-Encoding`
for that one request), range requests, and `Content-Length`.

Optional fallback: if the **File Transformation** plugin is installed, register the same
transformation with it via reflection so users of that ecosystem get one consistent path.
Direct on-disk edits of `index.html` are **not** performed.

The client script:

1. Waits for `document.body` and installs a `MutationObserver` on `body` (childList only,
   no subtree) to detect the `.playerStats` element being added.
2. When found, appends `<div class="dataFlow">` inside `.playerStats-content` after
   `.playerStats-stats`. jellyfin-web only rewrites `.playerStats-stats` innerHTML, so
   the sibling survives its 700 ms re-render.
3. Observes the `class` attribute of `.playerStats` to detect show/hide (`hide` class)
   and starts/stops polling accordingly.
4. Resolves its own `deviceId` via `window.ApiClient.deviceId()` and fetches session
   details via `ApiClient.getSessions({ deviceId })` every 10 s while visible to obtain
   `TranscodingInfo.Bitrate`, `NowPlayingItem` and media source bitrate.
5. Reaches the video element via `document.querySelector('video.htmlvideoplayer')` for
   `buffered` and, when `window.Hls` exists, hooks the active hls.js instance if
   reachable; otherwise falls back to Resource Timing.

## 7. Configuration (admin page)

- Enable/disable injection into the web client.
- Poll interval (default 2000 ms, min 1000).
- History length (default 300 s).
- Margin below required bitrate treated as "marginal" (default 20 %).
- Network interfaces to include for server totals (default: all non-loopback, up).
- Enable client-side (browser) measurement fallback (default on).

## 8. Non-functional requirements

Resource budget (verified in testing before release):

| Component | Budget |
|---|---|
| Server CPU | < 1 % of one core with 10 concurrent streams; middleware adds only an `Interlocked.Add` per write. |
| Server memory | < 2 MB steady state for 50 tracked devices at 300 samples (3 series × 8 bytes × 300 × 50 ≈ 360 KB plus overhead). |
| Server per request | No buffering of media bodies; no allocation per write on the hot path. `index.html` is the only buffered response. |
| Client CPU | < 2 % while the overlay is open on a 2020-era laptop; one canvas redraw per received poll or resize; zero timers while hidden. |
| Client network | One request per poll (default every 2 s) of < 2 KB; one `Sessions` call per 10 s. |
| Payload | `client.js` + `client.css` < 25 KB unminified, no third-party libraries. |

Other:

- Must not break playback if the plugin fails: all client code wrapped so exceptions never
  propagate; middleware never alters media responses beyond passing bytes through.
- Must work behind a reverse proxy and with a non-root `BaseUrl`.
- No PII stored; device ids only live in memory and are never logged at Info level.
- Strict `TreatWarningsAsErrors` C# build per the Jellyfin template, StyleCop enabled.

## 9. Acceptance criteria

1. Open any video in the web client, open Playback Info: a Data Flow section with a live
   graph appears within 2 s and updates every poll interval.
2. Resize the browser window: graph redraws at the new width, no blur on HiDPI.
3. Hover: tooltip shows time and all series values; cursor line follows the mouse.
4. Throttle the network (browser dev tools) below the media bitrate: fill turns amber then
   red, badge shows "starved"; remove throttle: returns to green.
5. Transcode a file: required-bitrate line matches `TranscodingInfo.Bitrate`.
6. Close overlay: DevTools shows no further `/DataFlow` requests and no timers.
7. Server CPU and memory within budgets under 10 concurrent playbacks.
8. Plugin loads on Jellyfin 12.0.x without "NotSupported" status; disabling the plugin
   leaves `index.html` untouched (nothing was written to disk).
