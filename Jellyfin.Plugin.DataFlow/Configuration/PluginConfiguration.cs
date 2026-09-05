using System;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.DataFlow.Configuration;

/// <summary>
/// Plugin configuration.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets a value indicating whether the client script is injected into the web client.
    /// </summary>
    public bool EnableInjection { get; set; } = true;

    /// <summary>
    /// Gets or sets the client poll interval in milliseconds (minimum 1000).
    /// </summary>
    public int PollIntervalMs { get; set; } = 2000;

    /// <summary>
    /// Gets or sets the number of one-second samples kept per device and for the server.
    /// </summary>
    public int HistorySeconds { get; set; } = 300;

    /// <summary>
    /// Gets or sets the margin, in percent below the required bitrate, still considered "marginal" rather than "starved".
    /// </summary>
    public int MarginPercent { get; set; } = 20;

    /// <summary>
    /// Gets or sets the network interface names included in server totals. Empty means all interfaces that are up and not loopback.
    /// </summary>
    public string[] NetworkInterfaces { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Gets or sets a value indicating whether the browser may fall back to its own measurement when the server sees no bytes.
    /// </summary>
    public bool EnableBrowserMeasurement { get; set; } = true;

    /// <summary>
    /// Gets the poll interval clamped to the supported range.
    /// </summary>
    /// <returns>The poll interval in milliseconds.</returns>
    public int GetEffectivePollIntervalMs() => Math.Clamp(PollIntervalMs, 1000, 60_000);

    /// <summary>
    /// Gets the history length clamped to the supported range.
    /// </summary>
    /// <returns>The history length in seconds.</returns>
    public int GetEffectiveHistorySeconds() => Math.Clamp(HistorySeconds, 60, 3600);

    /// <summary>
    /// Gets the margin clamped to the supported range.
    /// </summary>
    /// <returns>The margin in percent.</returns>
    public int GetEffectiveMarginPercent() => Math.Clamp(MarginPercent, 0, 90);
}
