using System;
using System.IO;
using System.Net.Mime;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.DataFlow.Metrics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace Jellyfin.Plugin.DataFlow.Api;

/// <summary>
/// Serves the client assets and the throughput samples.
/// </summary>
[ApiController]
[Route("DataFlow")]
[Produces(MediaTypeNames.Application.Json)]
public class DataFlowController : ControllerBase
{
    private static readonly string Version = typeof(DataFlowController).Assembly.GetName().Version?.ToString() ?? "0";
    private static readonly string ETag = "\"" + Version + "\"";
    private static readonly byte[] ClientScript = ReadResource("Client.client.js");
    private static readonly byte[] ClientStyles = ReadResource("Client.client.css");

    private readonly ThroughputStore _store;

    /// <summary>
    /// Initializes a new instance of the <see cref="DataFlowController"/> class.
    /// </summary>
    /// <param name="store">The throughput store.</param>
    public DataFlowController(ThroughputStore store)
    {
        _store = store;
    }

    /// <summary>
    /// Gets the client script injected into the web client.
    /// </summary>
    /// <returns>The script.</returns>
    [HttpGet("client.js")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    public IActionResult GetClientScript() => StaticAsset(ClientScript, "application/javascript; charset=utf-8");

    /// <summary>
    /// Gets the client stylesheet.
    /// </summary>
    /// <returns>The stylesheet.</returns>
    [HttpGet("client.css")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    public IActionResult GetClientStyles() => StaticAsset(ClientStyles, "text/css; charset=utf-8");

    /// <summary>
    /// Gets the settings the client needs.
    /// </summary>
    /// <returns>The client configuration.</returns>
    [HttpGet("Config")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<ClientConfig> GetConfig()
    {
        var cfg = Plugin.CurrentConfiguration;
        return new ClientConfig
        {
            PollIntervalMs = cfg.GetEffectivePollIntervalMs(),
            HistorySeconds = cfg.GetEffectiveHistorySeconds(),
            MarginPercent = cfg.GetEffectiveMarginPercent(),
            BrowserMeasurement = cfg.EnableBrowserMeasurement,
            Windows = [60, 120, 300],
            Version = Version
        };
    }

    /// <summary>
    /// Gets throughput samples for a device plus the server totals over the same range.
    /// </summary>
    /// <param name="deviceId">The client device id.</param>
    /// <param name="since">Return only buckets starting after this unix ms; omit for the full history.</param>
    /// <returns>The samples.</returns>
    [HttpGet("Samples")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult<ThroughputSnapshot> GetSamples([FromQuery] string deviceId, [FromQuery] long since = 0)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return BadRequest("deviceId is required");
        }

        Response.Headers.CacheControl = "no-store";
        return _store.Snapshot(deviceId, since, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    /// <summary>
    /// Lists the server's network interfaces (for the admin page).
    /// </summary>
    /// <returns>The interfaces.</returns>
    [HttpGet("Interfaces")]
    [Authorize(Policy = "RequiresElevation")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<NetworkInterfaceInfo[]> GetInterfaces()
    {
        var list = NetworkInterfaceSampler.ListInterfaces();
        var result = new NetworkInterfaceInfo[list.Count];
        for (int i = 0; i < list.Count; i++)
        {
            result[i] = new NetworkInterfaceInfo { Name = list[i].Name, Description = list[i].Description, Up = list[i].Up };
        }

        return result;
    }

    private IActionResult StaticAsset(byte[] bytes, string contentType)
    {
        Response.Headers.CacheControl = "public, max-age=3600";
        Response.Headers.ETag = ETag;
        if (Request.Headers.TryGetValue(HeaderNames.IfNoneMatch, out var inm) && string.Equals(inm.ToString(), ETag, StringComparison.Ordinal))
        {
            return StatusCode(StatusCodes.Status304NotModified);
        }

        return File(bytes, contentType);
    }

    private static byte[] ReadResource(string suffix)
    {
        var asm = typeof(DataFlowController).Assembly;
        string name = typeof(Plugin).Namespace + "." + suffix;
        using Stream? s = asm.GetManifestResourceStream(name);
        if (s is null)
        {
            return [];
        }

        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>
    /// Client-relevant settings.
    /// </summary>
    public sealed class ClientConfig
    {
        /// <summary>Gets or sets the poll interval in ms.</summary>
        [JsonPropertyName("pollIntervalMs")]
        public int PollIntervalMs { get; set; }

        /// <summary>Gets or sets the server history length in seconds.</summary>
        [JsonPropertyName("historySeconds")]
        public int HistorySeconds { get; set; }

        /// <summary>Gets or sets the marginal band in percent.</summary>
        [JsonPropertyName("marginPercent")]
        public int MarginPercent { get; set; }

        /// <summary>Gets or sets a value indicating whether browser-side measurement is allowed.</summary>
        [JsonPropertyName("browserMeasurement")]
        public bool BrowserMeasurement { get; set; }

        /// <summary>Gets or sets the selectable window lengths in seconds.</summary>
        [JsonPropertyName("windows")]
        public int[] Windows { get; set; } = [];

        /// <summary>Gets or sets the plugin version.</summary>
        [JsonPropertyName("version")]
        public string Version { get; set; } = string.Empty;
    }

    /// <summary>
    /// A network interface summary.
    /// </summary>
    public sealed class NetworkInterfaceInfo
    {
        /// <summary>Gets or sets the interface name.</summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>Gets or sets the interface description.</summary>
        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        /// <summary>Gets or sets a value indicating whether the interface is up.</summary>
        [JsonPropertyName("up")]
        public bool Up { get; set; }
    }
}
