using Jellyfin.Plugin.DataFlow.Metrics;
using Xunit;

namespace Jellyfin.Plugin.DataFlow.Tests;

public class ThroughputStoreTests
{
    private const long T = 1_700_000_000_000;

    [Fact]
    public void Snapshot_BeforeFirstTick_IsEmpty()
    {
        var store = new ThroughputStore(10);
        var snap = store.Snapshot("dev", 0, T);
        Assert.Empty(snap.ClientDown);
        Assert.Empty(snap.ServerUp);
        Assert.False(snap.DeviceKnown);
    }

    [Fact]
    public void Tick_DrainsAccumulatorsIntoAlignedBuckets()
    {
        var store = new ThroughputStore(10);
        var dev = store.GetOrCreate("dev", T);
        dev.AddDown(1000);
        dev.AddUp(10);
        store.Tick(T + 1000, 5000, 50);
        dev.AddDown(2000);
        store.Tick(T + 2000, 6000, 60);

        var snap = store.Snapshot("dev", 0, T + 2500);
        Assert.True(snap.DeviceKnown);
        Assert.Equal(2, snap.ClientDown.Length);
        Assert.Equal(new long[] { 1000, 2000 }, snap.ClientDown);
        Assert.Equal(new long[] { 10, 0 }, snap.ClientUp);
        Assert.Equal(new long[] { 5000, 6000 }, snap.ServerUp);
        Assert.Equal(new long[] { 50, 60 }, snap.ServerDown);
        Assert.Equal(T, snap.T0); // first bucket covers [T, T+1000)
        Assert.Equal(1000, snap.Interval);
    }

    [Fact]
    public void Snapshot_SinceCursor_ReturnsOnlyNewerBuckets()
    {
        var store = new ThroughputStore(10);
        var dev = store.GetOrCreate("dev", T);
        for (int i = 1; i <= 5; i++)
        {
            dev.AddDown(i * 100);
            store.Tick(T + (i * 1000L), i, i);
        }

        // Client has everything up to and including the bucket starting at T+2000 (i=3): pass its start as cursor.
        var snap = store.Snapshot("dev", T + 2000, T + 5500);
        Assert.Equal(new long[] { 400, 500 }, snap.ClientDown);
        Assert.Equal(T + 3000, snap.T0);

        // Cursor at the newest bucket start returns nothing new.
        var none = store.Snapshot("dev", T + 4000, T + 5500);
        Assert.Empty(none.ClientDown);

        // Cursor in the future returns nothing.
        Assert.Empty(store.Snapshot("dev", T + 99_000, T + 5500).ClientDown);
    }

    [Fact]
    public void Snapshot_UnknownDevice_ReturnsZerosOfSameLength()
    {
        var store = new ThroughputStore(10);
        store.Tick(T + 1000, 1, 2);
        store.Tick(T + 2000, 3, 4);
        var snap = store.Snapshot("nobody", 0, T + 2500);
        Assert.False(snap.DeviceKnown);
        Assert.Equal(new long[] { 0, 0 }, snap.ClientDown);
        Assert.Equal(new long[] { 1, 3 }, snap.ServerUp);
    }

    [Fact]
    public void Snapshot_LengthIsCappedByHistoryAndTicks()
    {
        var store = new ThroughputStore(4);
        for (int i = 1; i <= 2; i++)
        {
            store.Tick(T + (i * 1000L), i, i);
        }

        Assert.Equal(2, store.Snapshot("x", 0, T + 2500).ServerUp.Length);
        for (int i = 3; i <= 10; i++)
        {
            store.Tick(T + (i * 1000L), i, i);
        }

        var snap = store.Snapshot("x", 0, T + 10_500);
        Assert.Equal(new long[] { 7, 8, 9, 10 }, snap.ServerUp);
        Assert.Equal(T + 6000, snap.T0);
    }

    [Fact]
    public void Tick_EvictsIdleDevices()
    {
        var store = new ThroughputStore(10);
        store.GetOrCreate("idle", T);
        store.Tick(T + 1000, 0, 0);
        Assert.Equal(1, store.DeviceCount);
        store.Tick(T + (11 * 60_000L), 0, 0);
        Assert.Equal(0, store.DeviceCount);
    }
}
