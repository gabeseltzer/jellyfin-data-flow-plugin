using System;

namespace Jellyfin.Plugin.DataFlow.Metrics;

/// <summary>
/// Fixed-capacity ring buffer of per-second byte counts. Written by the sampler once per tick
/// and read by API requests; a short lock keeps snapshots consistent.
/// </summary>
public sealed class RingBuffer
{
    private readonly long[] _values;
    private readonly object _lock = new();
    private int _head; // index of the next slot to write
    private int _count;

    /// <summary>
    /// Initializes a new instance of the <see cref="RingBuffer"/> class.
    /// </summary>
    /// <param name="capacity">Number of samples to retain.</param>
    public RingBuffer(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _values = new long[capacity];
    }

    /// <summary>
    /// Gets the buffer capacity.
    /// </summary>
    public int Capacity => _values.Length;

    /// <summary>
    /// Gets the number of samples written so far, capped at <see cref="Capacity"/>.
    /// </summary>
    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _count;
            }
        }
    }

    /// <summary>
    /// Appends a sample, overwriting the oldest when full.
    /// </summary>
    /// <param name="value">The sample value.</param>
    public void Add(long value)
    {
        lock (_lock)
        {
            _values[_head] = value;
            _head = (_head + 1) % _values.Length;
            if (_count < _values.Length)
            {
                _count++;
            }
        }
    }

    /// <summary>
    /// Returns the most recent <paramref name="n"/> samples, oldest first. When fewer than
    /// <paramref name="n"/> samples exist the result is left-padded with zeros so that every
    /// buffer snapshot of the same length lines up in time.
    /// </summary>
    /// <param name="n">Number of samples wanted.</param>
    /// <returns>An array of exactly <paramref name="n"/> values (or <see cref="Capacity"/> if smaller).</returns>
    public long[] Latest(int n)
    {
        n = Math.Clamp(n, 0, _values.Length);
        var result = new long[n];
        lock (_lock)
        {
            int available = Math.Min(n, _count);
            int pad = n - available;
            int start = (_head - available + _values.Length) % _values.Length;
            for (int i = 0; i < available; i++)
            {
                result[pad + i] = _values[(start + i) % _values.Length];
            }
        }

        return result;
    }
}
