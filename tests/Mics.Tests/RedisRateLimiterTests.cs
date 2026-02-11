using Mics.Gateway.Infrastructure.Redis;
using Mics.Gateway.Metrics;

namespace Mics.Tests;

/// <summary>
/// 测试 Redis 限流器的本地降级逻辑
/// 注意：Redis 正常流程需要集成测试，这里仅测试降级和边界条件
/// </summary>
public sealed class RedisRateLimiterTests
{
    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _now;

        public ManualTimeProvider(long unixSeconds)
        {
            _now = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        }

        public void SetTime(long unixSeconds) => _now = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        public void Advance(TimeSpan delta) => _now = _now.Add(delta);

        public override DateTimeOffset GetUtcNow() => _now;
    }

    // 由于 StackExchange.Redis 的 IConnectionMultiplexer 和 IDatabase 接口极其复杂
    // 包含 300+ 个方法，手动 Mock 不现实且容易出错
    // 
    // 建议的测试策略：
    // 1. 使用真实 Redis 进行集成测试（推荐使用 Testcontainers）
    // 2. 测试本地降级逻辑的正确性（不依赖 Redis）
    // 3. 验证边界条件和异常处理
    //
    // 当前文件保留为占位符，实际测试应在以下场景中补充：
    // - tests/Mics.IntegrationTests/RedisRateLimiterIntegrationTests.cs (使用真实 Redis)
    // - 手动测试文档中验证降级行为
}
