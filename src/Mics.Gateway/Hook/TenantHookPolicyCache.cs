using System.Collections.Concurrent;
using Mics.Contracts.Hook.V1;

namespace Mics.Gateway.Hook;

internal interface ITenantHookPolicyCache
{
    HookPolicy Get(string tenantId);
    HookPolicy Resolve(string tenantId, TenantRuntimeConfig tenantConfig);
    void Update(string tenantId, TenantRuntimeConfig tenantConfig);
}

internal sealed class TenantHookPolicyCache : ITenantHookPolicyCache
{
    private readonly ConcurrentDictionary<string, HookPolicy> _cache = new(StringComparer.Ordinal);
    private readonly HookPolicyDefaults _defaults;

    public TenantHookPolicyCache(HookPolicyDefaults defaults)
    {
        _defaults = defaults;
    }

    public HookPolicy Get(string tenantId) =>
        _cache.TryGetValue(tenantId, out var policy)
            ? policy
            : HookPolicy.Resolve(tenantId, tenantConfig: null, _defaults);

    public HookPolicy Resolve(string tenantId, TenantRuntimeConfig tenantConfig) =>
        HookPolicy.Resolve(tenantId, tenantConfig, _defaults);

    public void Update(string tenantId, TenantRuntimeConfig tenantConfig) =>
        _cache[tenantId] = HookPolicy.Resolve(tenantId, tenantConfig, _defaults);
}

