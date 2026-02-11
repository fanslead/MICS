using System.Collections.Concurrent;
using Mics.Gateway.Metrics;
using StackExchange.Redis;

namespace Mics.Gateway.Infrastructure.Redis;

internal interface IRedisRateLimiter
{
    ValueTask<bool> TryConsumeTenantMessageAsync(string tenantId, int maxQps, CancellationToken cancellationToken);
}

internal sealed class RedisRateLimiter : IRedisRateLimiter
{
    private readonly IDatabase _db;
    private readonly MetricsRegistry _metrics;
    private readonly TimeProvider _timeProvider;

    private sealed class LocalWindowState
    {
        public long WindowSec;
        public int Count;
        public object Gate { get; } = new();
    }

    private readonly ConcurrentDictionary<string, LocalWindowState> _local = new(StringComparer.Ordinal);

    public RedisRateLimiter(IConnectionMultiplexer mux, MetricsRegistry metrics, TimeProvider timeProvider)
    {
        _db = mux.GetDatabase();
        _metrics = metrics;
        _timeProvider = timeProvider;
    }

    public async ValueTask<bool> TryConsumeTenantMessageAsync(string tenantId, int maxQps, CancellationToken cancellationToken)
    {
        if (maxQps <= 0)
        {
            return true;
        }

        var nowSec = _timeProvider.GetUtcNow().ToUnixTimeSeconds();
        var key = $"{tenantId}:rl:msg:{nowSec}";

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var current = await _db.StringIncrementAsync(key);
            if (current == 1)
            {
                await _db.KeyExpireAsync(key, TimeSpan.FromSeconds(2));
            }

            return current <= maxQps;
        }
        catch (RedisException)
        {
            _metrics.CounterInc("mics_redis_fallback_total", 1, ("tenantId", tenantId), ("op", "rate_limit"));
            return LocalTryConsume(tenantId, maxQps, nowSec);
        }
    }

    private bool LocalTryConsume(string tenantId, int maxQps, long nowSec)
    {
        var state = _local.GetOrAdd(tenantId, _ => new LocalWindowState { WindowSec = nowSec, Count = 0 });
        lock (state.Gate)
        {
            if (state.WindowSec != nowSec)
            {
                state.WindowSec = nowSec;
                state.Count = 0;
            }

            state.Count++;
            return state.Count <= maxQps;
        }
    }
}

