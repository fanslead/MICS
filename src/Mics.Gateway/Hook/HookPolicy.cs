using Mics.Contracts.Hook.V1;

namespace Mics.Gateway.Hook;

internal readonly record struct HookAcquirePolicy(int MaxConcurrency, TimeSpan QueueTimeout);

internal readonly record struct HookBreakerPolicy(int FailureThreshold, TimeSpan OpenDuration);

internal readonly record struct HookPolicy(HookAcquirePolicy Acquire, HookBreakerPolicy Breaker, bool SignRequired)
{
    public static HookPolicy Resolve(string tenantId, TenantRuntimeConfig? tenantConfig, HookPolicyDefaults defaults)
    {
        var maxConcurrency = defaults.TenantMaxConcurrencyFallback.TryGetValue(tenantId, out var v) && v > 0
            ? v
            : defaults.MaxConcurrencyDefault;

        var queueTimeout = defaults.QueueTimeoutDefault;
        var breakerThreshold = defaults.BreakerFailureThresholdDefault;
        var breakerOpen = defaults.BreakerOpenDurationDefault;
        var signRequired = defaults.SignRequiredDefault;

        if (tenantConfig is not null)
        {
            if (tenantConfig.HasHookMaxConcurrency && tenantConfig.HookMaxConcurrency > 0)
            {
                maxConcurrency = tenantConfig.HookMaxConcurrency;
            }

            if (tenantConfig.HasHookQueueTimeoutMs && tenantConfig.HookQueueTimeoutMs >= 0)
            {
                queueTimeout = TimeSpan.FromMilliseconds(Math.Clamp(tenantConfig.HookQueueTimeoutMs, 0, 10_000));
            }

            if (tenantConfig.HasHookBreakerFailureThreshold && tenantConfig.HookBreakerFailureThreshold > 0)
            {
                breakerThreshold = Math.Clamp(tenantConfig.HookBreakerFailureThreshold, 1, 100);
            }

            if (tenantConfig.HasHookBreakerOpenMs && tenantConfig.HookBreakerOpenMs >= 0)
            {
                breakerOpen = TimeSpan.FromMilliseconds(Math.Clamp(tenantConfig.HookBreakerOpenMs, 0, 60_000));
            }

            if (tenantConfig.HasHookSignRequired)
            {
                signRequired = tenantConfig.HookSignRequired;
            }
        }

        maxConcurrency = Math.Max(1, maxConcurrency);
        if (queueTimeout < TimeSpan.Zero) queueTimeout = TimeSpan.Zero;
        if (breakerOpen < TimeSpan.Zero) breakerOpen = TimeSpan.Zero;
        breakerThreshold = Math.Max(1, breakerThreshold);

        return new HookPolicy(
            new HookAcquirePolicy(maxConcurrency, queueTimeout),
            new HookBreakerPolicy(breakerThreshold, breakerOpen),
            signRequired);
    }
}

internal sealed record HookPolicyDefaults(
    int MaxConcurrencyDefault,
    IReadOnlyDictionary<string, int> TenantMaxConcurrencyFallback,
    TimeSpan QueueTimeoutDefault,
    int BreakerFailureThresholdDefault,
    TimeSpan BreakerOpenDurationDefault,
    bool SignRequiredDefault);

