using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.DataFlow.Metrics;

namespace Jellyfin.Plugin.DataFlow.Web;

/// <summary>
/// Write-only pass-through stream that attributes every written byte to a device.
/// No buffering; the hot path is a single interlocked add per write.
/// </summary>
public sealed class CountingStream : Stream
{
    private readonly Stream _inner;
    private readonly DeviceCounters _counters;

    /// <summary>
    /// Initializes a new instance of the <see cref="CountingStream"/> class.
    /// </summary>
    /// <param name="inner">The response body stream.</param>
    /// <param name="counters">The counters to credit.</param>
    public CountingStream(Stream inner, DeviceCounters counters)
    {
        _inner = inner;
        _counters = counters;
    }

    /// <inheritdoc />
    public override bool CanRead => false;

    /// <inheritdoc />
    public override bool CanSeek => false;

    /// <inheritdoc />
    public override bool CanWrite => _inner.CanWrite;

    /// <inheritdoc />
    public override long Length => throw new NotSupportedException();

    /// <inheritdoc />
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    /// <inheritdoc />
    public override void Flush() => _inner.Flush();

    /// <inheritdoc />
    public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /// <inheritdoc />
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count)
    {
        _inner.Write(buffer, offset, count);
        _counters.AddDown(count);
    }

    /// <inheritdoc />
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        _inner.Write(buffer);
        _counters.AddDown(buffer.Length);
    }

    /// <inheritdoc />
    public override void WriteByte(byte value)
    {
        _inner.WriteByte(value);
        _counters.AddDown(1);
    }

    /// <inheritdoc />
    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        await _inner.WriteAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
        _counters.AddDown(count);
    }

    /// <inheritdoc />
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await _inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        _counters.AddDown(buffer.Length);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        // The response body is owned by the server; never dispose it from here.
        base.Dispose(disposing);
    }
}
