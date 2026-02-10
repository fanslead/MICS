using StackExchange.Redis;

namespace Mics.Gateway.Infrastructure.Redis;

internal interface IRedisRateLimiter
{
    ValueTask<bool> TryConsumeTenantMessageAsync(string tenantId, int maxQps, CancellationToken cancellationToken);
}

internal sealed class RedisRateLimiter : IRedisRateLimiter
{
    private readonly IDatabase _db;

    public RedisRateLimiter(IConnectionMultiplexer mux)
    {
        _db = mux.GetDatabase();
    }

    public async ValueTask<bool> TryConsumeTenantMessageAsync(string tenantId, int maxQps, CancellationToken cancellationToken)
    {
        if (maxQps <= 0)
        {
            return true;
        }

        var nowSec = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var key = $"{tenantId}:rl:msg:{nowSec}";

        cancellationToken.ThrowIfCancellationRequested();

        var current = await _db.StringIncrementAsync(key);
        if (current == 1)
        {
            await _db.KeyExpireAsync(key, TimeSpan.FromSeconds(2));
        }

        return current <= maxQps;
    }
}

