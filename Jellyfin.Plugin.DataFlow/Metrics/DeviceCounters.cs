using System.Threading;

namespace Jellyfin.Plugin.DataFlow.Metrics;

/// <summary>
/// Byte accumulators and history for a single client device.
/// </summary>
public sealed class DeviceCounters
{
    private long _downAccumulator;
    private long _upAccumulator;
    private long _lastActivityMs;

    /// <summary>
    /// Initializes a new instance of the <see cref="DeviceCounters"/> class.
    /// </summary>
    /// <param name="historySeconds">Ring buffer length.</param>
    /// <param name="nowMs">Creation time in unix milliseconds.</param>
    public DeviceCounters(int historySeconds, long nowMs)
    {
        Down = new RingBuffer(historySeconds);
        Up = new RingBuffer(historySeconds);
        _lastActivityMs = nowMs;
    }

    /// <summary>
    /// Gets the per-second history of bytes sent to the client.
    /// </summary>
    public RingBuffer Down { get; }

    /// <summary>
    /// Gets the per-second history of bytes received from the client.
    /// </summary>
    public RingBuffer Up { get; }

    /// <summary>
    /// Gets the last time (unix ms) any bytes were attributed to this device.
    /// </summary>
    public long LastActivityMs => Volatile.Read(ref _lastActivityMs);

    /// <summary>
    /// Records bytes sent to the client. Hot path: one interlocked add.
    /// </summary>
    /// <param name="bytes">Byte count.</param>
    public void AddDown(long bytes) => Interlocked.Add(ref _downAccumulator, bytes);

    /// <summary>
    /// Records bytes received from the client.
    /// </summary>
    /// <param name="bytes">Byte count.</param>
    public void AddUp(long bytes) => Interlocked.Add(ref _upAccumulator, bytes);

    /// <summary>
    /// Marks the device as active.
    /// </summary>
    /// <param name="nowMs">Current unix ms.</param>
    public void Touch(long nowMs) => Volatile.Write(ref _lastActivityMs, nowMs);

    /// <summary>
    /// Moves the accumulated bytes into the history buffers as one bucket each.
    /// </summary>
    /// <param name="nowMs">Current unix ms.</param>
    public void Drain(long nowMs)
    {
        long down = Interlocked.Exchange(ref _downAccumulator, 0);
        long up = Interlocked.Exchange(ref _upAccumulator, 0);
        Down.Add(down);
        Up.Add(up);
        if (down > 0 || up > 0)
        {
            Touch(nowMs);
        }
    }
}
