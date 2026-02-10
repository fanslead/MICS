using StackExchange.Redis;

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

    public OnlineRouteStore(IConnectionMultiplexer mux)
    {
        _db = mux.GetDatabase();
    }

    public async ValueTask UpsertAsync(string tenantId, string userId, string deviceId, OnlineDeviceRoute route, CancellationToken cancellationToken)
    {
        var key = RedisKeys.OnlineUserHash(tenantId, userId);
        var value = OnlineDeviceRouteCodec.Encode(route);

        cancellationToken.ThrowIfCancellationRequested();
        await _db.HashSetAsync(key, deviceId, value);
    }

    public async ValueTask RemoveAsync(string tenantId, string userId, string deviceId, CancellationToken cancellationToken)
    {
        var key = RedisKeys.OnlineUserHash(tenantId, userId);
        cancellationToken.ThrowIfCancellationRequested();
        await _db.HashDeleteAsync(key, deviceId);
    }

    public async ValueTask<IReadOnlyDictionary<string, OnlineDeviceRoute>> GetAllDevicesAsync(string tenantId, string userId, CancellationToken cancellationToken)
    {
        var key = RedisKeys.OnlineUserHash(tenantId, userId);

        cancellationToken.ThrowIfCancellationRequested();
        var entries = await _db.HashGetAllAsync(key);

        var result = new Dictionary<string, OnlineDeviceRoute>(StringComparer.Ordinal);
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

        var batch = _db.CreateBatch();
        var tasks = new Task<HashEntry[]>[userIds.Count];

        for (var i = 0; i < userIds.Count; i++)
        {
            var userId = userIds[i];
            tasks[i] = batch.HashGetAllAsync(RedisKeys.OnlineUserHash(tenantId, userId));
        }

        batch.Execute();

        await Task.WhenAll(tasks);

        var result = new Dictionary<string, IReadOnlyList<OnlineDeviceRoute>>(StringComparer.Ordinal);
        for (var i = 0; i < userIds.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var entries = tasks[i].Result;
            if (entries.Length == 0)
            {
                continue;
            }

            List<OnlineDeviceRoute>? routes = null;
            foreach (var entry in entries)
            {
                if (!entry.Value.HasValue)
                {
                    continue;
                }

                if (OnlineDeviceRouteCodec.TryDecode(entry.Value.ToString(), out var route) && route is not null)
                {
                    routes ??= new List<OnlineDeviceRoute>(entries.Length);
                    routes.Add(route);
                }
            }

            if (routes is { Count: > 0 })
            {
                result[userIds[i]] = routes;
            }
        }

        return result;
    }

    public async ValueTask<long> GetDeviceCountAsync(string tenantId, string userId, CancellationToken cancellationToken)
    {
        var key = RedisKeys.OnlineUserHash(tenantId, userId);
        cancellationToken.ThrowIfCancellationRequested();
        return await _db.HashLengthAsync(key);
    }
}
