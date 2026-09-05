using Jellyfin.Plugin.DataFlow.Metrics;
using Xunit;

namespace Jellyfin.Plugin.DataFlow.Tests;

public class RingBufferTests
{
    [Fact]
    public void Latest_PadsWithZerosWhenNotFull()
    {
        var rb = new RingBuffer(5);
        rb.Add(1);
        rb.Add(2);
        Assert.Equal(new long[] { 0, 0, 0, 1, 2 }, rb.Latest(5));
        Assert.Equal(new long[] { 2 }, rb.Latest(1));
        Assert.Equal(2, rb.Count);
    }

    [Fact]
    public void Latest_WrapsAround()
    {
        var rb = new RingBuffer(3);
        for (long i = 1; i <= 7; i++)
        {
            rb.Add(i);
        }

        Assert.Equal(3, rb.Count);
        Assert.Equal(new long[] { 5, 6, 7 }, rb.Latest(3));
        Assert.Equal(new long[] { 6, 7 }, rb.Latest(2));
        Assert.Equal(new long[] { 5, 6, 7 }, rb.Latest(10)); // clamped to capacity
        Assert.Empty(rb.Latest(0));
    }
}
