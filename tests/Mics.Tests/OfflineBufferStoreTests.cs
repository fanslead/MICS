using Mics.Gateway.Offline;
using Xunit;

namespace Mics.Tests;

public sealed class OfflineBufferStoreTests
{
    [Fact]
    public void TryAdd_And_Drain_Roundtrip()
    {
        var store = new OfflineBufferStore(maxMessagesPerUser: 10, maxBytesPerUser: 10_000);

        Assert.True(store.TryAdd("t1", "u1", new byte[] { 1, 2, 3 }, TimeSpan.FromSeconds(10)));
        Assert.True(store.TryAdd("t1", "u1", new byte[] { 4, 5 }, TimeSpan.FromSeconds(10)));

        var drained = store.Drain("t1", "u1");
        Assert.Equal(2, drained.Count);
        Assert.Equal(new byte[] { 1, 2, 3 }, drained[0]);
        Assert.Equal(new byte[] { 4, 5 }, drained[1]);
        Assert.Empty(store.Drain("t1", "u1"));
    }

    [Fact]
    public void TryAdd_IsCapped_By_MaxMessages()
    {
        var store = new OfflineBufferStore(maxMessagesPerUser: 1, maxBytesPerUser: 10_000);

        Assert.True(store.TryAdd("t1", "u1", new byte[] { 1 }, TimeSpan.FromSeconds(10)));
        Assert.False(store.TryAdd("t1", "u1", new byte[] { 2 }, TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public void TryAdd_IsCapped_By_MaxBytes()
    {
        var store = new OfflineBufferStore(maxMessagesPerUser: 10, maxBytesPerUser: 3);

        Assert.True(store.TryAdd("t1", "u1", new byte[] { 1, 2 }, TimeSpan.FromSeconds(10)));
        Assert.False(store.TryAdd("t1", "u1", new byte[] { 3, 4 }, TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public void TryAdd_PrunesExpiredItems_BeforeApplyingLimits()
    {
        var store = new OfflineBufferStore(maxMessagesPerUser: 1, maxBytesPerUser: 10_000);

        Assert.True(store.TryAdd("t1", "u1", new byte[] { 1 }, TimeSpan.FromMilliseconds(-1)));
        Assert.True(store.TryAdd("t1", "u1", new byte[] { 2 }, TimeSpan.FromSeconds(10)));
        Assert.Single(store.Drain("t1", "u1"));
    }
}

