using Mics.Contracts.Hook.V1;
using Mics.Gateway.Hook;

namespace Mics.Tests;

public sealed class HookPolicyTests
{
    [Fact]
    public void Resolve_UsesTenantConfigOverrides_WhenPresent()
    {
        var defaults = new HookPolicyDefaults(
            MaxConcurrencyDefault: 32,
            TenantMaxConcurrencyFallback: new Dictionary<string, int> { ["t1"] = 16 },
            QueueTimeoutDefault: TimeSpan.FromMilliseconds(10),
            BreakerFailureThresholdDefault: 5,
            BreakerOpenDurationDefault: TimeSpan.FromSeconds(5),
            SignRequiredDefault: false);

        var cfg = new TenantRuntimeConfig
        {
            HookMaxConcurrency = 4,
            HookQueueTimeoutMs = 0,
            HookBreakerFailureThreshold = 2,
            HookBreakerOpenMs = 1234,
            HookSignRequired = true,
        };

        var policy = HookPolicy.Resolve("t1", cfg, defaults);

        Assert.Equal(4, policy.Acquire.MaxConcurrency);
        Assert.Equal(TimeSpan.Zero, policy.Acquire.QueueTimeout);
        Assert.Equal(2, policy.Breaker.FailureThreshold);
        Assert.Equal(TimeSpan.FromMilliseconds(1234), policy.Breaker.OpenDuration);
        Assert.True(policy.SignRequired);
    }

    [Fact]
    public void Resolve_FallsBackToPerTenantMap_WhenUnset()
    {
        var defaults = new HookPolicyDefaults(
            MaxConcurrencyDefault: 32,
            TenantMaxConcurrencyFallback: new Dictionary<string, int> { ["t1"] = 16 },
            QueueTimeoutDefault: TimeSpan.FromMilliseconds(10),
            BreakerFailureThresholdDefault: 5,
            BreakerOpenDurationDefault: TimeSpan.FromSeconds(5),
            SignRequiredDefault: false);

        var policy = HookPolicy.Resolve("t1", tenantConfig: null, defaults);
        Assert.Equal(16, policy.Acquire.MaxConcurrency);
    }
}

