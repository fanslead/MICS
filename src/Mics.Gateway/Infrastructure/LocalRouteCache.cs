using Microsoft.Extensions.Caching.Memory;
using Mics.Gateway.Infrastructure.Redis;

namespace Mics.Gateway.Infrastructure;

internal interface ILocalRouteCache
{
    bool TryGet(string tenantId, string userId, out IReadOnlyDictionary<string, OnlineDeviceRoute> routes);
    void Set(string tenantId, string userId, IReadOnlyDictionary<string, OnlineDeviceRoute> routes, TimeSpan ttl);
    void Invalidate(string tenantId, string userId);
}

internal sealed class NoopLocalRouteCache : ILocalRouteCache
{
    public bool TryGet(string tenantId, string userId, out IReadOnlyDictionary<string, OnlineDeviceRoute> routes)
    {
        routes = null!;
        return false;
    }

    public void Set(string tenantId, string userId, IReadOnlyDictionary<string, OnlineDeviceRoute> routes, TimeSpan ttl)
    {
    }

    public void Invalidate(string tenantId, string userId)
    {
    }
}

internal sealed class LocalRouteCache : ILocalRouteCache
{
    private readonly MemoryCache _cache;

    public LocalRouteCache(long maxSizeBytes)
    {
        maxSizeBytes = Math.Clamp(maxSizeBytes, 1L, 1_024L * 1_024L * 1_024L);
        _cache = new MemoryCache(new MemoryCacheOptions
        {
            SizeLimit = maxSizeBytes,
            ExpirationScanFrequency = TimeSpan.FromSeconds(10),
        });
    }

    public bool TryGet(string tenantId, string userId, out IReadOnlyDictionary<string, OnlineDeviceRoute> routes)
    {
        if (_cache.TryGetValue(CacheKey(tenantId, userId), out var cached))
        {
            routes = (IReadOnlyDictionary<string, OnlineDeviceRoute>)cached!;
            return true;
        }

        routes = null!;
        return false;
    }

    public void Set(string tenantId, string userId, IReadOnlyDictionary<string, OnlineDeviceRoute> routes, TimeSpan ttl)
    {
        if (routes.Count == 0 || ttl <= TimeSpan.Zero)
        {
            return;
        }

        var estimatedSize = Math.Max(1, routes.Count) * 256;

        _cache.Set(
            CacheKey(tenantId, userId),
            routes,
            new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = ttl,
                Size = estimatedSize,
            });
    }

    public void Invalidate(string tenantId, string userId)
    {
        _cache.Remove(CacheKey(tenantId, userId));
    }

    private static string CacheKey(string tenantId, string userId) => $"{tenantId}:{userId}";
}

