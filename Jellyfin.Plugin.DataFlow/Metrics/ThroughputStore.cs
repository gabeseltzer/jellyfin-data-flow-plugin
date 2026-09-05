using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Jellyfin.Plugin.DataFlow.Metrics;

/// <summary>
/// In-memory throughput history: per-device counters plus server-wide NIC totals.
/// </summary>
public sealed class ThroughputStore
{
    /// <summary>
    /// Length of one bucket in milliseconds.
    /// </summary>
    public const int IntervalMs = 1000;

    private static readonly TimeSpan IdleEviction = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<string, DeviceCounters> _devices = new(StringComparer.Ordinal);
    private readonly int _historySeconds;
    private readonly object _tickLock = new();
    private long _lastTickMs;
    private int _tickCount;

    /// <summary>
    /// Initializes a new instance of the <see cref="ThroughputStore"/> class.
    /// </summary>
    public ThroughputStore()
        : this(Plugin.CurrentConfiguration.GetEffectiveHistorySeconds())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ThroughputStore"/> class.
    /// </summary>
    /// <param name="historySeconds">Number of one-second buckets retained.</param>
    public ThroughputStore(int historySeconds)
    {
        _historySeconds = Math.Max(1, historySeconds);
        ServerUp = new RingBuffer(_historySeconds);
        ServerDown = new RingBuffer(_historySeconds);
    }

    /// <summary>
    /// Gets the server-wide bytes-sent history.
    /// </summary>
    public RingBuffer ServerUp { get; }

    /// <summary>
    /// Gets the server-wide bytes-received history.
    /// </summary>
    public RingBuffer ServerDown { get; }

    /// <summary>
    /// Gets the number of tracked devices.
    /// </summary>
    public int DeviceCount => _devices.Count;

    /// <summary>
    /// Gets the history length in seconds.
    /// </summary>
    public int HistorySeconds => _historySeconds;

    /// <summary>
    /// Gets the time (unix ms) of the most recent tick, or 0 before the first.
    /// </summary>
    public long LastTickMs
    {
        get
        {
            lock (_tickLock)
            {
                return _lastTickMs;
            }
        }
    }

    /// <summary>
    /// Gets or creates the counters for a device.
    /// </summary>
    /// <param name="deviceId">The client device id.</param>
    /// <param name="nowMs">Current unix ms.</param>
    /// <returns>The device counters.</returns>
    public DeviceCounters GetOrCreate(string deviceId, long nowMs)
    {
        var counters = _devices.GetOrAdd(deviceId, static (_, args) => new DeviceCounters(args.History, args.Now), (History: _historySeconds, Now: nowMs));
        counters.Touch(nowMs);
        return counters;
    }

    /// <summary>
    /// Tries to get the counters for a device without creating them.
    /// </summary>
    /// <param name="deviceId">The client device id.</param>
    /// <param name="counters">The counters when found.</param>
    /// <returns><c>true</c> when the device is tracked.</returns>
    public bool TryGet(string deviceId, out DeviceCounters? counters) => _devices.TryGetValue(deviceId, out counters);

    /// <summary>
    /// Closes the current one-second bucket for every series and evicts idle devices.
    /// </summary>
    /// <param name="nowMs">Current unix ms.</param>
    /// <param name="serverUpBytes">Bytes the server sent since the previous tick.</param>
    /// <param name="serverDownBytes">Bytes the server received since the previous tick.</param>
    public void Tick(long nowMs, long serverUpBytes, long serverDownBytes)
    {
        lock (_tickLock)
        {
            ServerUp.Add(serverUpBytes);
            ServerDown.Add(serverDownBytes);

            List<string>? toEvict = null;
            foreach (var kvp in _devices)
            {
                kvp.Value.Drain(nowMs);
                if (nowMs - kvp.Value.LastActivityMs > IdleEviction.TotalMilliseconds)
                {
                    (toEvict ??= []).Add(kvp.Key);
                }
            }

            if (toEvict is not null)
            {
                foreach (var key in toEvict)
                {
                    _devices.TryRemove(key, out _);
                }
            }

            _lastTickMs = nowMs;
            if (_tickCount < _historySeconds)
            {
                _tickCount++;
            }
        }
    }

    /// <summary>
    /// Builds a snapshot of all buckets closed after <paramref name="sinceMs"/>.
    /// </summary>
    /// <param name="deviceId">The client device id.</param>
    /// <param name="sinceMs">Only buckets starting after this unix ms are returned; 0 for everything.</param>
    /// <param name="nowMs">Current unix ms.</param>
    /// <returns>The snapshot.</returns>
    public ThroughputSnapshot Snapshot(string deviceId, long sinceMs, long nowMs)
    {
        long lastTick;
        int available;
        lock (_tickLock)
        {
            lastTick = _lastTickMs;
            available = _tickCount;
        }

        int n;
        if (lastTick == 0)
        {
            n = 0;
        }
        else
        {
            // Bucket i (0 = newest) starts at lastTick - (i + 1) * IntervalMs.
            long newestStart = lastTick - IntervalMs;
            long span = newestStart - sinceMs;
            // Return buckets that start strictly after sinceMs (the cursor is the start of the newest bucket the client has).
            n = span <= 0 ? 0 : (int)Math.Min(available, ((span - 1) / IntervalMs) + 1);
        }

        bool known = _devices.TryGetValue(deviceId, out var device);
        var snapshot = new ThroughputSnapshot
        {
            Now = nowMs,
            Interval = IntervalMs,
            T0 = lastTick - ((long)n * IntervalMs),
            ServerUp = ServerUp.Latest(n),
            ServerDown = ServerDown.Latest(n),
            ClientDown = device?.Down.Latest(n) ?? new long[n],
            ClientUp = device?.Up.Latest(n) ?? new long[n],
            DeviceKnown = known
        };
        return snapshot;
    }
}
