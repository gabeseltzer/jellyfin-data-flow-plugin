# Implementation plan

See `SPEC.md` for what we are building and `docs/RESEARCH.md` for why the approach below
is feasible. Each phase ends in a runnable, committed state.

## Repository layout (target)

```
Jellyfin.Plugin.DataFlow.sln
Jellyfin.Plugin.DataFlow/
  Jellyfin.Plugin.DataFlow.csproj      net9.0, Jellyfin.Controller 10.11.0 (jf12 switch later)
  Plugin.cs                            BasePlugin<PluginConfiguration>, IHasWebPages
  PluginServiceRegistrator.cs          DI: IStartupFilter, IHostedService, ThroughputStore
  Configuration/PluginConfiguration.cs
  Configuration/configPage.html        admin settings page (embedded)
  Web/ScriptInjectionStartupFilter.cs  rewrites /web/index.html to add <script>
  Web/ByteCountingMiddleware.cs        counts media bytes per deviceId
  Web/CountingStream.cs                Stream wrapper, Interlocked.Add on Write
  Metrics/ThroughputStore.cs           per-device ring buffers + NIC totals
  Metrics/RingBuffer.cs
  Metrics/SamplerHostedService.cs      1 Hz timer: drain counters, read NIC stats, evict
  Api/DataFlowController.cs            /DataFlow/client.js, client.css, Samples, Config
  Client/client.js                     injected script (embedded resource)
  Client/client.css
Jellyfin.Plugin.DataFlow.Tests/        xunit: RingBuffer, ThroughputStore, route matcher, index.html rewrite
build.yaml, meta.json template, .editorconfig, .gitignore, README.md
scripts/dev-jellyfin.ps1               run Jellyfin 10.11 in Docker with plugin dir mounted
```

## Phase 0: toolchain (½ day) — devcontainer

Decision (2026-09-05): all development happens in a VS Code devcontainer; no .NET SDK on
the host. `.devcontainer/docker-compose.yml` runs two services:

- `dev`: `mcr.microsoft.com/devcontainers/dotnet:1-9.0` + ffmpeg/jq/curl, repo mounted at
  `/workspaces/jellyfin-data-flow-plugin`, Docker socket via `docker-outside-of-docker`
  so scripts can restart the Jellyfin container.
- `jellyfin`: `jellyfin/jellyfin:10.11`, port 8096 forwarded to the host browser,
  `dist/plugins` mounted at `/config/plugins`, `media/` mounted read-only, state in `.dev/`.

Scripts: `scripts/post-create.sh` (one-time setup, generates test media),
`scripts/make-test-media.sh` (ffmpeg clips at 1.5 / 6 / 20 Mbps),
`scripts/deploy.sh` (build, copy DLL + meta.json into `dist/plugins`, restart Jellyfin, tail logs).

- Checkpoint: devcontainer opens, Jellyfin wizard completes at http://localhost:8096 with
  `/media` as a Movies library, empty template plugin shows in Dashboard > Plugins.

## Phase 1: plugin skeleton + injection (1 day)

- Create project from the template shape (csproj, Plugin.cs, configuration).
- `ScriptInjectionStartupFilter`: port the JS Injector approach (buffer only index.html,
  strip Accept-Encoding/Range, fix Content-Length, drop ETag). Config flag to disable.
- `DataFlowController.GetClientScript` serving an embedded `client.js` with a hello-world
  that logs once. `[AllowAnonymous]`, `Cache-Control: public, max-age=3600`, ETag from
  assembly version.
- Checkpoint: open web client, see the console log; `index.html` on disk unchanged.

## Phase 2: server-side measurement (1 day)

- `RingBuffer<long>` fixed-size, lock-free single-writer / snapshot reader.
- `ThroughputStore`: `ConcurrentDictionary<string, DeviceCounters>` where counters hold
  `long down`, `long up` accumulators plus two ring buffers; plus server `up`/`down` ring
  buffers. `Snapshot(deviceId, sinceMs)` returns compact arrays.
- `ByteCountingMiddleware` (added inside the same startup filter, after index rewrite):
  fast path = path prefix check on `/Videos/` or `/Audio/` and segment/stream suffix; only
  then read `deviceId` from query, wrap `Response.Body` in `CountingStream`, and add
  `Request.ContentLength ?? 0` + header estimate to `up`. Must also work when a downstream
  handler uses `SendFileAsync` (Kestrel bypasses the Body stream for `IHttpResponseBodyFeature.SendFileAsync`).
  Mitigation: replace `IHttpResponseBodyFeature` with a wrapper that counts in
  `SendFileAsync` too (`StreamResponseBodyFeature` over the counting stream). Verify with
  progressive direct play, which uses `FileStreamResult`/`PhysicalFile`.
- `SamplerHostedService`: `PeriodicTimer(1s)`; drain accumulators into buckets; read
  `NetworkInterface` stats (cache the interface list, refresh every 60 s); evict idle keys.
- `GET /DataFlow/Samples` and `/DataFlow/Config`.
- Unit tests: ring buffer wrap-around, snapshot windows, route matcher, index.html rewrite
  idempotency.
- Checkpoint: play a video, `curl /DataFlow/Samples?deviceId=...` shows non-zero buckets
  that match the file's bitrate.

## Phase 3: client panel and graph (1.5 days)

- `client.js` (IIFE, no build step, ES2018 to match jellyfin-web's browser support):
  - Overlay discovery via `MutationObserver` on `body` (childList) and class-attribute
    observer for show/hide.
  - Panel DOM: header row (`.playerStats-stat-header` style), canvas, legend/values row,
    tooltip element. Inject `client.css` via `<link>` once.
  - Poller: `fetch` with the ApiClient token (`ApiClient.accessToken()`) every
    `pollInterval`, `since` cursor so payloads are deltas; on first open fetch full window.
  - Session info every 10 s: `ApiClient.getSessions({ deviceId })` to get
    `TranscodingInfo.Bitrate`, `PlayState.PlayMethod`, `NowPlayingItem.MediaSources`.
  - Browser-side measurement: `PerformanceObserver` on `resource` entries filtered to media
    URLs (bytes via `transferSize`, else `encodedBodySize`); `video.buffered` for buffer
    health. Used when the server series is all zeros (proxy cache) and for buffer health.
  - Renderer: single `requestAnimationFrame` scheduled only when data or size changes.
    Draw order: gridlines, colour-coded fill under client-download (segments split at
    threshold crossings), series lines, average line, required line, cursor + tooltip.
    `ResizeObserver` on the panel; scale canvas by `devicePixelRatio`.
  - Pointer handling: `pointermove`/`pointerleave`/`touchstart`; nearest sample lookup by
    x. Hover disabled on TV layout.
  - Interaction: click legend entries to toggle series (persist in `localStorage`); click
    window label to cycle 1/2/5 min.
- Checkpoint: acceptance criteria 1-6 in `SPEC.md` pass manually in Chrome and Firefox.

## Phase 4: configuration page + polish (½ day)

- `configPage.html` following template conventions (`Dashboard.showLoadingMsg`,
  `ApiClient.getPluginConfiguration`), fields from SPEC §7, NIC multi-select filled from
  a `/DataFlow/Interfaces` admin endpoint.
- Optional File Transformation registration (reflection, guarded, logged at Debug).
- Localisation: strings in one object so they can be translated later.

## Phase 5: resource verification and release (½ day)

- Measure server CPU/RSS with 10 simultaneous `ffplay`/curl streams, before/after plugin.
- Measure client CPU with Chrome performance panel, overlay open vs closed.
- Record results in `docs/PERF.md`; adjust intervals if over budget.
- `build.yaml`, `meta.json`, GitHub Actions workflow producing zip + `manifest.json`
  (multi-ABI recipe), README with install instructions and screenshots.
- Tag v0.1.0.

## Risks and mitigations

| Risk | Mitigation |
|---|---|
| Kestrel `SendFileAsync` bypasses `Response.Body` so progressive streams count zero | Wrap `IHttpResponseBodyFeature` as described in Phase 2; test direct play explicitly. |
| Response compression middleware sits inside our middleware, so counted bytes are pre-compression | Media is not compressed (`ResponseCompression` excludes video MIME types); m3u8 is negligible. Accept. |
| jellyfin-web restructures `playerStats` DOM in a future release | Selectors isolated in one `adapter` object; degrade to "not available" instead of erroring. |
| Reverse proxy or CDN serves media so the server sees no bytes | Browser-side Resource Timing fallback; note limitation in README. |
| `IStartupFilter` ordering changes in Jellyfin 12 | Same approach shipped by JS Injector for 12; keep the jf12 build target and CI matrix. |
| Users on native clients expect the feature | README states web-only scope up front. |

## Open questions for the user

1. Docker available locally for a test Jellyfin, or should we target an existing install?
2. Is client upload worth a series at all, or should it be tooltip-only to reduce clutter?
   Plan assumes a thin series that is off by default in the legend.
3. Should the default window be 2 min (plan) or match the visible-overlay time only?
