using StackExchange.Redis;
using Mics.Gateway.Config;
using Mics.Gateway.Infrastructure;
using Mics.Gateway.Metrics;

namespace Mics.Gateway.Infrastructure.Redis;

internal interface IOnlineRouteStore
{
    ValueTask UpsertAsync(string tenantId, string userId, string deviceId, OnlineDeviceRoute route, CancellationToken cancellationToken);
    ValueTask RemoveAsync(string tenantId, string userId, string deviceId, CancellationToken cancellationToken);
    ValueTask<IReadOnlyDictionary<string, OnlineDeviceRoute>> GetAllDevicesAsync(string tenantId, string userId, CancellationToken cancellationToken);
    ValueTask<IReadOnlyDictionary<string, IReadOnlyList<OnlineDeviceRoute>>> GetAllDevicesForUsersAsync(string tenantId, IReadOnlyList<string> userIds, CancellationToken cancellationToken);
    ValueTask<long> GetDeviceCountAsync(string tenantId, string userId, CancellationToken cancellationToken);
}

internal sealed class OnlineRouteStore : IOnlineRouteStore
{
    private readonly IDatabase _db;
    private readonly ILocalRouteCache _cache;
    private readonly TimeSpan _cacheTtl;
    private readonly MetricsRegistry _metrics;

    public OnlineRouteStore(IConnectionMultiplexer mux, ILocalRouteCache cache, GatewayOptions options, MetricsRegistry metrics)
    {
        _db = mux.GetDatabase();
        _cache = cache;
        var ttlSeconds = Math.Clamp(options.LocalRouteCacheTtlSeconds, 0, 60);
        _cacheTtl = ttlSeconds <= 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(ttlSeconds);
        _metrics = metrics;
    }

    public async ValueTask UpsertAsync(string tenantId, string userId, string deviceId, OnlineDeviceRoute route, CancellationToken cancellationToken)
    {
        var key = RedisKeys.OnlineUserHash(tenantId, userId);
        var value = OnlineDeviceRouteCodec.Encode(route);

        cancellationToken.ThrowIfCancellationRequested();
        await _db.HashSetAsync(key, deviceId, value);
        _cache.Invalidate(tenantId, userId);
    }

    public async ValueTask RemoveAsync(string tenantId, string userId, string deviceId, CancellationToken cancellationToken)
    {
        var key = RedisKeys.OnlineUserHash(tenantId, userId);
        cancellationToken.ThrowIfCancellationRequested();
        await _db.HashDeleteAsync(key, deviceId);
        _cache.Invalidate(tenantId, userId);
    }

    public async ValueTask<IReadOnlyDictionary<string, OnlineDeviceRoute>> GetAllDevicesAsync(string tenantId, string userId, CancellationToken cancellationToken)
    {
        if (_cacheTtl > TimeSpan.Zero && _cache.TryGet(tenantId, userId, out var cached))
        {
            return cached;
        }

        var key = RedisKeys.OnlineUserHash(tenantId, userId);

        cancellationToken.ThrowIfCancellationRequested();

        HashEntry[] entries;
        try
        {
            entries = await _db.HashGetAllAsync(key);
        }
        catch (RedisException)
        {
            _metrics.CounterInc("mics_redis_fallback_total", 1, ("tenantId", tenantId), ("op", "route_get"));
            return new Dictionary<string, OnlineDeviceRoute>(StringComparer.Ordinal);
        }

        var result = new Dictionary<string, OnlineDeviceRoute>(entries.Length, StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (!entry.Value.HasValue)
            {
                continue;
            }

            if (OnlineDeviceRouteCodec.TryDecode(entry.Value.ToString(), out var route) && route is not null)
            {
                result[entry.Name.ToString()] = route;
            }
        }

        if (_cacheTtl > TimeSpan.Zero && result.Count > 0)
        {
            _cache.Set(tenantId, userId, result, _cacheTtl);
        }

        return result;
    }

    public async ValueTask<IReadOnlyDictionary<string, IReadOnlyList<OnlineDeviceRoute>>> GetAllDevicesForUsersAsync(
        string tenantId,
        IReadOnlyList<string> userIds,
        CancellationToken cancellationToken)
    {
        if (userIds.Count == 0)
        {
            return new Dictionary<string, IReadOnlyList<OnlineDeviceRoute>>(StringComparer.Ordinal);
        }

        var result = new Dictionary<string, IReadOnlyList<OnlineDeviceRoute>>(userIds.Count, StringComparer.Ordinal);

        int[]? missIndices = null;
        var missCount = 0;

        if (_cacheTtl > TimeSpan.Zero)
        {
            missIndices = new int[userIds.Count];

            for (var i = 0; i < userIds.Count; i++)
            {
                var userId = userIds[i];
                if (_cache.TryGet(tenantId, userId, out var cached) && cached.Count > 0)
                {
                    var routes = new OnlineDeviceRoute[cached.Count];
                    var idx = 0;
                    foreach (var route in cached.Values)
                    {
                        routes[idx++] = route;
                    }

                    result[userId] = routes;
                    continue;
                }

                missIndices[missCount++] = i;
            }

            if (missCount == 0)
            {
                return result;
            }
        }
        else
        {
            missIndices = new int[userIds.Count];
            for (var i = 0; i < userIds.Count; i++)
            {
                missIndices[i] = i;
            }

            missCount = userIds.Count;
        }

        var batch = _db.CreateBatch();
        var tasks = new Task<HashEntry[]>[missCount];

        for (var i = 0; i < missCount; i++)
        {
            var userId = userIds[missIndices[i]];
            tasks[i] = batch.HashGetAllAsync(RedisKeys.OnlineUserHash(tenantId, userId));
        }

        batch.Execute();

        try
        {
            await Task.WhenAll(tasks);
        }
        catch (RedisException)
        {
            _metrics.CounterInc("mics_redis_fallback_total", 1, ("tenantId", tenantId), ("op", "route_get_batch"));
            return new Dictionary<string, IReadOnlyList<OnlineDeviceRoute>>(StringComparer.Ordinal);
        }

        for (var i = 0; i < missCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var userId = userIds[missIndices[i]];

            HashEntry[] entries;
            try
            {
                entries = tasks[i].Result;
            }
            catch (RedisException)
            {
                // A single failed task shouldn't bring down the whole batch; treat that user as offline.
                _metrics.CounterInc("mics_redis_fallback_total", 1, ("tenantId", tenantId), ("op", "route_get_batch_item"));
                continue;
            }
            if (entries.Length == 0)
            {
                continue;
            }

            List<OnlineDeviceRoute>? routes = null;
            Dictionary<string, OnlineDeviceRoute>? cacheRoutes = null;
            foreach (var entry in entries)
            {
                if (!entry.Value.HasValue)
                {
                    continue;
                }

                if (OnlineDeviceRouteCodec.TryDecode(entry.Value.ToString(), out var route) && route is not null)
                {
                    routes ??= new List<OnlineDeviceRoute>(Math.Min(entries.Length, 4));
                    routes.Add(route);

                    if (_cacheTtl > TimeSpan.Zero)
                    {
                        cacheRoutes ??= new Dictionary<string, OnlineDeviceRoute>(entries.Length, StringComparer.Ordinal);
                        cacheRoutes[entry.Name.ToString()] = route;
                    }
                }
            }

            if (routes is { Count: > 0 })
            {
                result[userId] = routes;

                if (cacheRoutes is { Count: > 0 })
                {
                    _cache.Set(tenantId, userId, cacheRoutes, _cacheTtl);
                }
            }
        }

        return result;
    }

    public async ValueTask<long> GetDeviceCountAsync(string tenantId, string userId, CancellationToken cancellationToken)
    {
        var key = RedisKeys.OnlineUserHash(tenantId, userId);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return await _db.HashLengthAsync(key);
        }
        catch (RedisException)
        {
            _metrics.CounterInc("mics_redis_fallback_total", 1, ("tenantId", tenantId), ("op", "route_len"));
            return 0;
        }
    }
}
