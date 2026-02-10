using Mics.Gateway.Hook;
using Mics.Gateway.Metrics;

namespace Mics.Tests;

public sealed class HookConcurrencyLimiterTests
{
    [Fact]
    public async Task TryAcquire_RespectsMax_AndQueueTimeout()
    {
        var metrics = new MetricsRegistry();
        var limiter = new HookConcurrencyLimiter(metrics);

        await using var a = await limiter.TryAcquireAsync("t1", HookOperation.Auth, new HookAcquirePolicy(1, TimeSpan.Zero), CancellationToken.None);
        Assert.NotNull(a);

        await using var b = await limiter.TryAcquireAsync("t1", HookOperation.Auth, new HookAcquirePolicy(1, TimeSpan.Zero), CancellationToken.None);
        Assert.Null(b);

        var text = metrics.CollectPrometheusText();
        Assert.Contains("mics_hook_limiter_rejected_total{tenant=\"t1\",op=\"Auth\",reason=\"queue_timeout\"} 1", text);

        await using var c = await limiter.TryAcquireAsync("t2", HookOperation.Auth, new HookAcquirePolicy(2, TimeSpan.Zero), CancellationToken.None);
        Assert.NotNull(c);
        await using var d = await limiter.TryAcquireAsync("t2", HookOperation.Auth, new HookAcquirePolicy(2, TimeSpan.Zero), CancellationToken.None);
        Assert.NotNull(d);
        await using var e = await limiter.TryAcquireAsync("t2", HookOperation.Auth, new HookAcquirePolicy(2, TimeSpan.Zero), CancellationToken.None);
        Assert.Null(e);
    }

    [Fact]
    public async Task TryAcquire_IsolatedByOperation()
    {
        var limiter = new HookConcurrencyLimiter(new MetricsRegistry());

        await using var a = await limiter.TryAcquireAsync("t1", HookOperation.Auth, new HookAcquirePolicy(1, TimeSpan.Zero), CancellationToken.None);
        Assert.NotNull(a);

        // Different op: should use different semaphore
        await using var b = await limiter.TryAcquireAsync("t1", HookOperation.CheckMessage, new HookAcquirePolicy(1, TimeSpan.Zero), CancellationToken.None);
        Assert.NotNull(b);
    }
}
