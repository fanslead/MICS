using StackExchange.Redis;

namespace Mics.Gateway.Infrastructure.Redis;

internal interface IMessageDeduplicator
{
    /// <summary>
    /// Returns true if this MsgId is new (marked as seen), false if duplicate.
    /// </summary>
    ValueTask<bool> TryMarkAsync(string tenantId, string msgId, TimeSpan ttl, CancellationToken cancellationToken);
}

internal sealed class RedisMessageDeduplicator : IMessageDeduplicator
{
    private readonly IDatabase _db;

    public RedisMessageDeduplicator(IConnectionMultiplexer mux)
    {
        _db = mux.GetDatabase();
    }

    public async ValueTask<bool> TryMarkAsync(string tenantId, string msgId, TimeSpan ttl, CancellationToken cancellationToken)
    {
        var key = $"{tenantId}:dedup:{msgId}";
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            return await _db.StringSetAsync(key, "1", ttl, When.NotExists);
        }
        catch (RedisException)
        {
            // Fail-open to avoid blocking core chain.
            return true;
        }
    }
}

internal sealed class InMemoryMessageDeduplicator : IMessageDeduplicator
{
    private sealed record Item(long ExpiresAtMs);

    private readonly Dictionary<(string TenantId, string MsgId), Item> _items = new();
    private readonly object _lock = new();

    public ValueTask<bool> TryMarkAsync(string tenantId, string msgId, TimeSpan ttl, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var expiresAt = now + (long)ttl.TotalMilliseconds;

        lock (_lock)
        {
            // Cleanup opportunistically for this tenant/msg
            if (_items.TryGetValue((tenantId, msgId), out var existing) && existing.ExpiresAtMs >= now)
            {
                return ValueTask.FromResult(false);
            }

            _items[(tenantId, msgId)] = new Item(expiresAt);
            return ValueTask.FromResult(true);
        }
    }
}

