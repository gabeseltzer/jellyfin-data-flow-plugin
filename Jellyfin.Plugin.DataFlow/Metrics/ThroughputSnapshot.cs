using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.DataFlow.Metrics;

/// <summary>
/// Compact wire format for the <c>/DataFlow/Samples</c> endpoint. All series have the same
/// length; element <c>i</c> covers the second starting at <c>T0 + i * Interval</c>.
/// </summary>
public sealed class ThroughputSnapshot
{
    /// <summary>
    /// Gets or sets the server time (unix ms) when the snapshot was taken.
    /// </summary>
    [JsonPropertyName("now")]
    public long Now { get; set; }

    /// <summary>
    /// Gets or sets the bucket length in milliseconds.
    /// </summary>
    [JsonPropertyName("interval")]
    public int Interval { get; set; }

    /// <summary>
    /// Gets or sets the start time (unix ms) of the first bucket.
    /// </summary>
    [JsonPropertyName("t0")]
    public long T0 { get; set; }

    /// <summary>
    /// Gets or sets bytes sent to the client per bucket.
    /// </summary>
    [JsonPropertyName("clientDown")]
    public long[] ClientDown { get; set; } = [];

    /// <summary>
    /// Gets or sets bytes received from the client per bucket.
    /// </summary>
    [JsonPropertyName("clientUp")]
    public long[] ClientUp { get; set; } = [];

    /// <summary>
    /// Gets or sets bytes sent by the server on its network interfaces per bucket.
    /// </summary>
    [JsonPropertyName("serverUp")]
    public long[] ServerUp { get; set; } = [];

    /// <summary>
    /// Gets or sets bytes received by the server on its network interfaces per bucket.
    /// </summary>
    [JsonPropertyName("serverDown")]
    public long[] ServerDown { get; set; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether the server has seen any bytes for this device at all.
    /// </summary>
    [JsonPropertyName("deviceKnown")]
    public bool DeviceKnown { get; set; }
}
