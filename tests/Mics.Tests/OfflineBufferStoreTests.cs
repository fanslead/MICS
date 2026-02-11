using Mics.Gateway.Offline;
using Google.Protobuf;
using Xunit;

namespace Mics.Tests;

public sealed class OfflineBufferStoreTests
{
    [Fact]
    public void TryAdd_And_Drain_Roundtrip()
    {
        var store = new OfflineBufferStore(maxMessagesPerUser: 10, maxBytesPerUser: 10_000);

        Assert.True(store.TryAdd("t1", "u1", ByteString.CopyFrom(new byte[] { 1, 2, 3 }), TimeSpan.FromSeconds(10)));
        Assert.True(store.TryAdd("t1", "u1", ByteString.CopyFrom(new byte[] { 4, 5 }), TimeSpan.FromSeconds(10)));

        var drained = store.Drain("t1", "u1");
        Assert.Equal(2, drained.Count);
        Assert.Equal(ByteString.CopyFrom(new byte[] { 1, 2, 3 }), drained[0]);
        Assert.Equal(ByteString.CopyFrom(new byte[] { 4, 5 }), drained[1]);
        Assert.Empty(store.Drain("t1", "u1"));
    }

    [Fact]
    public void TryAdd_IsCapped_By_MaxMessages()
    {
        var store = new OfflineBufferStore(maxMessagesPerUser: 1, maxBytesPerUser: 10_000);

        Assert.True(store.TryAdd("t1", "u1", ByteString.CopyFrom(new byte[] { 1 }), TimeSpan.FromSeconds(10)));
        Assert.False(store.TryAdd("t1", "u1", ByteString.CopyFrom(new byte[] { 2 }), TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public void TryAdd_IsCapped_By_MaxBytes()
    {
        var store = new OfflineBufferStore(maxMessagesPerUser: 10, maxBytesPerUser: 3);

        Assert.True(store.TryAdd("t1", "u1", ByteString.CopyFrom(new byte[] { 1, 2 }), TimeSpan.FromSeconds(10)));
        Assert.False(store.TryAdd("t1", "u1", ByteString.CopyFrom(new byte[] { 3, 4 }), TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public void TryAdd_PrunesExpiredItems_BeforeApplyingLimits()
    {
        var store = new OfflineBufferStore(maxMessagesPerUser: 1, maxBytesPerUser: 10_000);

        Assert.True(store.TryAdd("t1", "u1", ByteString.CopyFrom(new byte[] { 1 }), TimeSpan.FromMilliseconds(-1)));
        Assert.True(store.TryAdd("t1", "u1", ByteString.CopyFrom(new byte[] { 2 }), TimeSpan.FromSeconds(10)));
        Assert.Single(store.Drain("t1", "u1"));
    }

    [Fact]
    public void TryAdd_Evicts_Lru_User_When_MaxUsersPerTenant_Exceeded()
    {
        var store = new OfflineBufferStore(maxMessagesPerUser: 10, maxBytesPerUser: 10_000, maxUsersPerTenant: 2);

        Assert.True(store.TryAdd("t1", "u1", ByteString.CopyFrom(new byte[] { 1 }), TimeSpan.FromSeconds(10)));
        Assert.True(store.TryAdd("t1", "u2", ByteString.CopyFrom(new byte[] { 2 }), TimeSpan.FromSeconds(10)));

        // Touch u1 so u2 becomes LRU.
        Assert.True(store.TryAdd("t1", "u1", ByteString.CopyFrom(new byte[] { 3 }), TimeSpan.FromSeconds(10)));

        // Adding a 3rd distinct user should evict u2.
        Assert.True(store.TryAdd("t1", "u3", ByteString.CopyFrom(new byte[] { 4 }), TimeSpan.FromSeconds(10)));

        Assert.Empty(store.Drain("t1", "u2"));

        var u1 = store.Drain("t1", "u1");
        Assert.Equal(2, u1.Count);

        var u3 = store.Drain("t1", "u3");
        Assert.Single(u3);
    }

    [Fact]
    public void TryAdd_MaxUsersPerTenant_Is_Isolated_By_Tenant()
    {
        var store = new OfflineBufferStore(maxMessagesPerUser: 10, maxBytesPerUser: 10_000, maxUsersPerTenant: 1);

        Assert.True(store.TryAdd("t1", "u1", ByteString.CopyFrom(new byte[] { 1 }), TimeSpan.FromSeconds(10)));
        Assert.True(store.TryAdd("t2", "u1", ByteString.CopyFrom(new byte[] { 2 }), TimeSpan.FromSeconds(10)));

        // Exceed cap for t1 only.
        Assert.True(store.TryAdd("t1", "u2", ByteString.CopyFrom(new byte[] { 3 }), TimeSpan.FromSeconds(10)));

        Assert.Empty(store.Drain("t1", "u1"));
        Assert.Single(store.Drain("t1", "u2"));
        Assert.Single(store.Drain("t2", "u1"));
    }
}

