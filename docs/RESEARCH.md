# Research notes: how Jellyfin plugins work (2026-09-05)

## Plugin basics

- Plugins are .NET class libraries loaded by the server via `AssemblyLoadContext`. Template:
  https://github.com/jellyfin/jellyfin-plugin-template
- This plugin targets **net10.0** and references `Jellyfin.Controller` / `Jellyfin.Model`
  `12.0.0` with `<ExcludeAssets>runtime</ExcludeAssets>`. `targetAbi` in `build.yaml` is
  `12.0.0.0`. Pinning the package to the `.0` of the line lets one DLL load across the
  whole line (a newer host satisfies an older ref).
- Plugins are "strictly one major version compatible", so ship separate artifacts per
  server major. 0.1.x (net9.0 / ABI 10.11.0.0) stays the 10.11 build; 0.2.x is the 12.0
  build. `scripts/package.sh` merges new versions into the existing `manifest.json` rather
  than replacing it, so both entries stay published and Jellyfin picks by ABI.
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
- Local machine has .NET SDKs 6/7/8 only; **.NET 10 SDK must be installed** to build.

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

## Jellyfin 12.0 retarget (2026-09-13)

Jellyfin 12.0 shipped 2026-09-08. The version scheme changed from `10.X.Y` to `X.Y`, so the
next line after 10.11 is 12.0 (10.12 was skipped). What actually changed for this plugin:

- **Server moved to .NET 10.** Plugins must retarget `net10.0` and rebuild; the `global.json`
  in jellyfin/jellyfin pins SDK `10.0.0` with `rollForward: latestMinor`.
- **`targetAbi` is now `12.0.0.0`** and `Jellyfin.Controller` / `Jellyfin.Model` are `12.0.0`.
- **No API break for anything this plugin uses.** Verified against tag `v12.0`:
  - `IPluginServiceRegistrator.RegisterServices(IServiceCollection, IServerApplicationHost)`
    is byte-identical to 10.11.
  - `IHasWebPages` / `PluginPageInfo` are still `MediaBrowser.Model.Plugins`, `BasePlugin<T>`
    still `MediaBrowser.Common.Plugins`.
  - `Policies.RequiresElevation` still exists (in `MediaBrowser.Common.Api`); the string
    literal the controller uses is unchanged.
  - `IStartupFilter` registration and ASP.NET middleware are framework, not Jellyfin, API.
- **Media route templates are unchanged**, so `MediaRouteMatcher` still matches:
  `Videos/{id}/stream[.container]`, `Videos/{id}/{master,main,live}.m3u8`,
  `{Videos,Audio}/{id}/hls1/{playlistId}/{segmentId}.{container}`.
- **jellyfin-web still uses the same hooks**: `.playerStats`, `.playerStats-content`,
  `.playerStats-tv` and the `hide` class are unchanged in `src/components/playerstats/`, and
  `ServerConnections.js` still assigns `window.ApiClient`. The 12.0 diff to `playerstats.js`
  only regroups the transcode/target labels it renders; the plugin reads bitrate from the
  `/Sessions` API rather than scraping the DOM, so it is unaffected.

The 12.0 breaking changes that *do* affect plugins — `IItemRepository` (alternate versions and
playlist contents moved out of the parent item), `IUserManager`, `IAuthenticationProvider`,
the new `ISearchEngine` — are all unused here.
