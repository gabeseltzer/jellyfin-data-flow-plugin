# Research notes: how Jellyfin plugins work (2026-09-05)

## Plugin basics

- Plugins are .NET class libraries loaded by the server via `AssemblyLoadContext`. Template:
  https://github.com/jellyfin/jellyfin-plugin-template
- Current template targets **net9.0**, references `Jellyfin.Controller` and `Jellyfin.Model`
  10.11.x with `<ExcludeAssets>runtime</ExcludeAssets>`. `targetAbi` in `build.yaml` is
  `10.11.0.0`. Latest 10.11 NuGet at time of writing: 10.11.11. Pinning the package to
  `10.11.0` lets one DLL load across all 10.11.x (a newer host satisfies an older ref).
- Jellyfin 12 (net10.0) is in RC; the JS Injector plugin already multi-targets with an
  MSBuild property switch (`JellyfinTarget=jf10|jf12`). Plugins are "strictly one major
  version compatible", so ship separate artifacts per version.
- Required pieces: `Plugin : BasePlugin<PluginConfiguration>` (Name, Id GUID, ctor with
  `IApplicationPaths`, `IXmlSerializer`), `PluginConfiguration : BasePluginConfiguration`,
  optional embedded `configPage.html` exposed via `IHasWebPages`.
- `IPluginServiceRegistrator.RegisterServices(IServiceCollection, IServerApplicationHost)`
  is called from `ApplicationHost.Init()` during ASP.NET `ConfigureServices`, so anything
  registered there (including `IHostedService` and **`IStartupFilter`**) takes effect.
- Controllers deriving from `ControllerBase` in plugin assemblies are auto-discovered
  (`GetApiPluginAssemblies()` feeds `AddJellyfinApi`). Use `[Authorize]` / `[AllowAnonymous]`.
- Packaging: `meta.json` + DLL in a zip; `manifest.json` for a plugin repository. Manual
  install: drop a folder into `<data>/plugins/<Name_version>/` and restart.
- Local machine has .NET SDKs 6/7/8 only; **.NET 9 SDK must be installed** to build.

## Getting JavaScript into jellyfin-web

There is no official hook. Three community approaches:

1. **ASP.NET middleware via `IStartupFilter`** (used by n00bcodr/Jellyfin-JavaScript-Injector
   v4, works on 10.11 and 12). Middleware runs outermost, detects `/web/index.html`,
   `/web/`, `/web` (suffix match so BaseUrl prefixes work), swaps `Response.Body` for a
   `MemoryStream`, strips `Accept-Encoding`, `Range`, `If-Range` from the request, then
   injects `<script>` before `</body>`, fixes `Content-Length`, drops `ETag`/`Last-Modified`.
   Nothing on disk changes. **Chosen approach.**
2. **File Transformation plugin** (IAmParadox27/jellyfin-plugin-file-transformation).
   Dependent plugins register via reflection: find the assembly whose FullName contains
   `.FileTransformation`, get type `Jellyfin.Plugin.FileTransformation.PluginInterface`,
   call static `RegisterTransformation(JObject payload)` with
   `{id, fileNamePattern, callbackAssembly, callbackClass, callbackMethod}`; the callback
   receives `{contents}` and returns new contents. Optional integration.
3. Direct edit of `jellyfin-web/index.html` on disk. Fragile (permissions, Docker,
   upgrades overwrite). Not doing this.

Serving the script: a plugin controller endpoint with `[AllowAnonymous]` returning
`application/javascript` (JS Injector does `GET /JavaScriptInjector/public.js`).

## The Playback Info overlay (jellyfin-web `src/components/playerstats/playerstats.js`)

- `init()` appends `<div class="playerStats">` (plus `playerStats-tv` on TV layout) to
  `document.body`, containing `.playerStats-content` which holds an optional
  `.playerStats-closeButton` and `.playerStats-stats`.
- `enabled(bool)` toggles the `hide` class and binds/unbinds a `timeupdate` listener on the
  player; `renderPlayerStats` is throttled to 700 ms and calls `getStats()`.
- `getStats()` = `Promise.all([player.getStats(), getSession()])`; session from
  `apiClient.getSessions()` cached 10 s; categories built by `getTranscodingStats`
  (`session.TranscodingInfo.*`), `getMediaSourceStats` (`playbackManager.currentMediaSource()`
  gives `Bitrate` and per-stream `BitRate`), `getSyncPlayStats`.
- `renderStats(elem, categories)` sets `innerHTML` of `.playerStats-stats` each render.
  Anything we add **outside** `.playerStats-stats` (a sibling inside `.playerStats-content`)
  survives. Rows: `.playerStats-stat` with `.playerStats-stat-label` + `.playerStats-stat-value`;
  header `.playerStats-stat-header`. Overlay CSS: `rgba(28,28,28,.8)` background,
  `font-size: 84%`, `.playerStats-stats { max-width: 50em }`.
- `player.getStats()` returns `{ categories: [{ name, subText?, stats: [{label, value}] }] }`.
  `htmlVideoPlayer.getStats()` reports dropped/corrupted frames and "HLS"/"Video" but no
  bandwidth. hls.js is loaded dynamically (`window.Hls = hls`), instance kept in
  `this._hlsPlayer` (not exposed). Video element: `<video class="htmlvideoplayer">` inside
  `.videoPlayerContainer`.
- `window.ApiClient` is global in jellyfin-web; `ApiClient.deviceId()` and
  `ApiClient.getSessions()` are available to injected scripts. `playbackManager` is a module
  import, not a global.

## Where bytes can be counted server-side

- Progressive: `GET/HEAD /Videos/{itemId}/stream` and `/stream.{container}`; HLS:
  `/Videos/{itemId}/master.m3u8`, `/main.m3u8`, segments
  `/Videos/{itemId}/hls1/{playlistId}/{segmentId}.{container}`; same shapes under `/Audio`.
- Stream routes have no `[Authorize]`; identification via query `deviceId`, `playSessionId`,
  `mediaSourceId`, `api_key`. `deviceId` is the stable join key with the client.
- `GET /Sessions` (authorized) returns `SessionInfoDto` with `TranscodingInfo`
  (`Bitrate`, `Framerate`, `CompletionPercentage`, `IsVideoDirect`, ...) and `PlayState`
  (`PlaySessionId`, `PlayMethod`). No bandwidth data exists in core Jellyfin.
- Middleware wrapping `HttpContext.Response.Body` with a counting `Stream` is the cheapest
  way to count response bytes without buffering. Kestrel exposes no per-request byte
  counters to plugins otherwise.
- Server totals: `System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()`
  then `GetIPStatistics().BytesSent / BytesReceived`; cross-platform, cheap at 1 Hz.

## Prior art

- No existing plugin draws a live bandwidth graph in the player. Playback Reporting and
  Flowfin/jellyfin-plugin-stats do historical playback stats; JellyWatch shows per-session
  bitrate from `/Sessions`. The feature request "Monitor server real-time status" is open.

## Sources

- https://jellyfin.org/docs/general/server/plugins/
- https://github.com/jellyfin/jellyfin-plugin-template
- https://github.com/n00bcodr/Jellyfin-JavaScript-Injector
- https://github.com/IAmParadox27/jellyfin-plugin-file-transformation
- https://github.com/jellyfin/jellyfin-web (playerstats.js, playerstats.scss, htmlVideoPlayer/plugin.js)
- https://github.com/jellyfin/jellyfin (Jellyfin.Server/Startup.cs, ApplicationHost.cs, VideosController.cs, DynamicHlsController.cs, SessionController.cs, TranscodingInfo.cs)
- https://www.nuget.org/packages/Jellyfin.Controller
- https://gist.github.com/IDisposable/31b194e3f6dc5acbb0e08009b6c800bd (multi-ABI build recipe)
