using Mics.Contracts.Hook.V1;
using Mics.Gateway.Hook;

namespace Mics.Tests;

/// <summary>
/// 测试租户 Hook 策略缓存：配置更新、降级策略、租户隔离
/// </summary>
public sealed class TenantHookPolicyCacheTests
{
    private static HookPolicyDefaults CreateDefaults()
    {
        return new HookPolicyDefaults(
            MaxConcurrencyDefault: 32,
            TenantMaxConcurrencyFallback: new Dictionary<string, int>
            {
                ["t1"] = 16,
                ["t2"] = 8,
            },
            QueueTimeoutDefault: TimeSpan.FromMilliseconds(100),
            BreakerFailureThresholdDefault: 5,
            BreakerOpenDurationDefault: TimeSpan.FromSeconds(5),
            SignRequiredDefault: false);
    }

    /// <summary>
    /// 测试从租户配置获取策略（覆盖默认值）
    /// </summary>
    [Fact]
    public void Resolve_UsesTenantConfigOverrides()
    {
        // Arrange
        var defaults = CreateDefaults();
        var cache = new TenantHookPolicyCache(defaults);

        var tenantConfig = new TenantRuntimeConfig
        {
            HookMaxConcurrency = 4,
            HookQueueTimeoutMs = 50,
            HookBreakerFailureThreshold = 2,
            HookBreakerOpenMs = 1000,
            HookSignRequired = true,
        };

        // Act
        var policy = cache.Resolve("t1", tenantConfig);

        // Assert
        Assert.Equal(4, policy.Acquire.MaxConcurrency);
        Assert.Equal(TimeSpan.FromMilliseconds(50), policy.Acquire.QueueTimeout);
        Assert.Equal(2, policy.Breaker.FailureThreshold);
        Assert.Equal(TimeSpan.FromMilliseconds(1000), policy.Breaker.OpenDuration);
        Assert.True(policy.SignRequired);
    }

    /// <summary>
    /// 测试租户配置为空时，使用每租户回退值
    /// </summary>
    [Fact]
    public void Resolve_NullConfig_FallbackToPerTenantDefault()
    {
        // Arrange
        var defaults = CreateDefaults();
        var cache = new TenantHookPolicyCache(defaults);

        // Act
        var policy1 = cache.Resolve("t1", tenantConfig: null);
        var policy2 = cache.Resolve("t2", tenantConfig: null);
        var policy3 = cache.Resolve("t-unknown", tenantConfig: null);

        // Assert
        Assert.Equal(16, policy1.Acquire.MaxConcurrency); // 从 TenantMaxConcurrencyFallback
        Assert.Equal(8, policy2.Acquire.MaxConcurrency);
        Assert.Equal(32, policy3.Acquire.MaxConcurrency); // 无回退值，使用全局默认

        Assert.Equal(TimeSpan.FromMilliseconds(100), policy1.Acquire.QueueTimeout); // 默认值
        Assert.Equal(5, policy1.Breaker.FailureThreshold);
        Assert.Equal(TimeSpan.FromSeconds(5), policy1.Breaker.OpenDuration);
        Assert.False(policy1.SignRequired);
    }

    /// <summary>
    /// 测试部分配置覆盖（未设置的字段使用默认值）
    /// </summary>
    [Fact]
    public void Resolve_PartialConfig_MergesWithDefaults()
    {
        // Arrange
        var defaults = CreateDefaults();
        var cache = new TenantHookPolicyCache(defaults);

        var tenantConfig = new TenantRuntimeConfig
        {
            HookMaxConcurrency = 10, // 仅覆盖并发数
        };

        // Act
        var policy = cache.Resolve("t1", tenantConfig);

        // Assert
        Assert.Equal(10, policy.Acquire.MaxConcurrency); // 使用租户配置
        Assert.Equal(TimeSpan.FromMilliseconds(100), policy.Acquire.QueueTimeout); // 使用默认值
        Assert.Equal(5, policy.Breaker.FailureThreshold);
        Assert.False(policy.SignRequired);
    }

    /// <summary>
    /// 测试配置边界值校验
    /// </summary>
    [Fact]
    public void Resolve_BoundaryValues_Clamped()
    {
        // Arrange
        var defaults = CreateDefaults();
        var cache = new TenantHookPolicyCache(defaults);

        var tenantConfig = new TenantRuntimeConfig
        {
            HookMaxConcurrency = -1, // 负数会被忽略，回退到租户默认值
            HookQueueTimeoutMs = -100, // 负数
            HookBreakerFailureThreshold = 0, // 零会被忽略
            HookBreakerOpenMs = -1, // 负数
        };

        // Act
        var policy = cache.Resolve("t1", tenantConfig);

        // Assert
        // 负数配置会被忽略，回退到租户默认值或全局默认值
        Assert.Equal(16, policy.Acquire.MaxConcurrency); // t1 的回退值
        Assert.Equal(TimeSpan.Zero, policy.Acquire.QueueTimeout); // 负数被 Clamp 到 0
        Assert.Equal(5, policy.Breaker.FailureThreshold); // 零会被忽略，使用默认值
        Assert.Equal(TimeSpan.Zero, policy.Breaker.OpenDuration); // 负数被 Clamp 到 0
    }

    /// <summary>
    /// 测试配置上限校验
    /// </summary>
    [Fact]
    public void Resolve_UpperBounds_Clamped()
    {
        // Arrange
        var defaults = CreateDefaults();
        var cache = new TenantHookPolicyCache(defaults);

        var tenantConfig = new TenantRuntimeConfig
        {
            HookQueueTimeoutMs = 99999, // 超出上限 10000ms
            HookBreakerFailureThreshold = 999, // 超出上限 100
            HookBreakerOpenMs = 999999, // 超出上限 60000ms
        };

        // Act
        var policy = cache.Resolve("t1", tenantConfig);

        // Assert
        Assert.Equal(TimeSpan.FromMilliseconds(10000), policy.Acquire.QueueTimeout); // 上限 10s
        Assert.Equal(100, policy.Breaker.FailureThreshold); // 上限 100
        Assert.Equal(TimeSpan.FromMilliseconds(60000), policy.Breaker.OpenDuration); // 上限 60s
    }

    /// <summary>
    /// 测试缓存更新机制
    /// </summary>
    [Fact]
    public void Update_RefreshesCache()
    {
        // Arrange
        var defaults = CreateDefaults();
        var cache = new TenantHookPolicyCache(defaults);

        var config1 = new TenantRuntimeConfig
        {
            HookMaxConcurrency = 10,
        };

        // Act - 初始更新
        cache.Update("t1", config1);
        var policy1 = cache.Get("t1");

        // 更新配置
        var config2 = new TenantRuntimeConfig
        {
            HookMaxConcurrency = 20,
        };

        cache.Update("t1", config2);
        var policy2 = cache.Get("t1");

        // Assert
        Assert.Equal(10, policy1.Acquire.MaxConcurrency);
        Assert.Equal(20, policy2.Acquire.MaxConcurrency);
    }

    /// <summary>
    /// 测试 Get 方法在未更新时使用默认值
    /// </summary>
    [Fact]
    public void Get_WithoutUpdate_UsesDefaults()
    {
        // Arrange
        var defaults = CreateDefaults();
        var cache = new TenantHookPolicyCache(defaults);

        // Act - 未调用 Update
        var policy1 = cache.Get("t1");
        var policy2 = cache.Get("t-unknown");

        // Assert
        Assert.Equal(16, policy1.Acquire.MaxConcurrency); // 回退到 TenantMaxConcurrencyFallback
        Assert.Equal(32, policy2.Acquire.MaxConcurrency); // 使用全局默认
    }

    /// <summary>
    /// 测试租户隔离：不同租户独立缓存
    /// </summary>
    [Fact]
    public void Cache_DifferentTenants_Isolated()
    {
        // Arrange
        var defaults = CreateDefaults();
        var cache = new TenantHookPolicyCache(defaults);

        var config1 = new TenantRuntimeConfig { HookMaxConcurrency = 5 };
        var config2 = new TenantRuntimeConfig { HookMaxConcurrency = 15 };

        // Act
        cache.Update("t1", config1);
        cache.Update("t2", config2);

        var policy1 = cache.Get("t1");
        var policy2 = cache.Get("t2");

        // Assert
        Assert.Equal(5, policy1.Acquire.MaxConcurrency);
        Assert.Equal(15, policy2.Acquire.MaxConcurrency);
    }

    /// <summary>
    /// 测试并发更新的线程安全
    /// </summary>
    [Fact]
    public async Task Cache_ConcurrentUpdate_ThreadSafe()
    {
        // Arrange
        var defaults = CreateDefaults();
        var cache = new TenantHookPolicyCache(defaults);
        const int concurrency = 10;

        // Act - 并发更新不同租户
        var tasks = Enumerable.Range(0, concurrency).Select(async i =>
        {
            await Task.Yield();

            var config = new TenantRuntimeConfig
            {
                HookMaxConcurrency = i + 1,
            };

            cache.Update($"t{i}", config);

            var policy = cache.Get($"t{i}");
            Assert.Equal(i + 1, policy.Acquire.MaxConcurrency);
        }).ToArray();

        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// 测试 optional 字段未设置时的行为
    /// </summary>
    [Fact]
    public void Resolve_OptionalFieldsUnset_UsesDefaults()
    {
        // Arrange
        var defaults = CreateDefaults();
        var cache = new TenantHookPolicyCache(defaults);

        // 创建空配置（所有 optional 字段都未设置）
        var tenantConfig = new TenantRuntimeConfig
        {
            HookBaseUrl = "http://hook:8080",
            HeartbeatTimeoutSeconds = 30,
        };

        // Act
        var policy = cache.Resolve("t1", tenantConfig);

        // Assert - 应全部使用默认值或回退值
        Assert.Equal(16, policy.Acquire.MaxConcurrency); // t1 的回退值
        Assert.Equal(TimeSpan.FromMilliseconds(100), policy.Acquire.QueueTimeout);
        Assert.Equal(5, policy.Breaker.FailureThreshold);
        Assert.Equal(TimeSpan.FromSeconds(5), policy.Breaker.OpenDuration);
        Assert.False(policy.SignRequired);
    }

    /// <summary>
    /// 测试签名要求配置
    /// </summary>
    [Fact]
    public void Resolve_SignRequired_RespectsTenantConfig()
    {
        // Arrange
        var defaults = new HookPolicyDefaults(
            MaxConcurrencyDefault: 32,
            TenantMaxConcurrencyFallback: new Dictionary<string, int>(),
            QueueTimeoutDefault: TimeSpan.FromMilliseconds(100),
            BreakerFailureThresholdDefault: 5,
            BreakerOpenDurationDefault: TimeSpan.FromSeconds(5),
            SignRequiredDefault: true); // 全局默认要求签名

        var cache = new TenantHookPolicyCache(defaults);

        // Act
        var policy1 = cache.Resolve("t1", tenantConfig: null);

        var config2 = new TenantRuntimeConfig
        {
            HookSignRequired = false, // 租户禁用签名
        };

        var policy2 = cache.Resolve("t2", config2);

        // Assert
        Assert.True(policy1.SignRequired, "应使用全局默认（需要签名）");
        Assert.False(policy2.SignRequired, "应使用租户配置（不需要签名）");
    }

    /// <summary>
    /// 测试 QueueTimeout 为 0 的特殊情况（禁用排队）
    /// </summary>
    [Fact]
    public void Resolve_QueueTimeoutZero_DisablesQueue()
    {
        // Arrange
        var defaults = CreateDefaults();
        var cache = new TenantHookPolicyCache(defaults);

        var tenantConfig = new TenantRuntimeConfig
        {
            HookQueueTimeoutMs = 0, // 禁用排队
        };

        // Act
        var policy = cache.Resolve("t1", tenantConfig);

        // Assert
        Assert.Equal(TimeSpan.Zero, policy.Acquire.QueueTimeout);
    }
}
