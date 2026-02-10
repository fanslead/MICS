using System.Reflection;
using Mics.Gateway.Hook;
using Mics.Gateway.Metrics;

namespace Mics.Tests;

public sealed class HookConcurrencyLimiterUpdateTests
{
    [Fact]
    public async Task TryAcquire_DoesNotReplaceSemaphore_WhenMaxChanges()
    {
        var limiter = new HookConcurrencyLimiter(new MetricsRegistry());

        await using (var a = await limiter.TryAcquireAsync("t1", HookOperation.Auth, new HookAcquirePolicy(2, TimeSpan.Zero), CancellationToken.None))
        {
            Assert.NotNull(a);
        }

        var sem1 = GetSemaphore(limiter, "t1", HookOperation.Auth);

        await using (var b = await limiter.TryAcquireAsync("t1", HookOperation.Auth, new HookAcquirePolicy(1, TimeSpan.Zero), CancellationToken.None))
        {
            Assert.NotNull(b);
        }

        var sem2 = GetSemaphore(limiter, "t1", HookOperation.Auth);
        Assert.Same(sem1, sem2);
    }

    private static SemaphoreSlim GetSemaphore(HookConcurrencyLimiter limiter, string tenantId, HookOperation op)
    {
        var field = typeof(HookConcurrencyLimiter).GetField("_entries", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        var dict = (System.Collections.IDictionary)field!.GetValue(limiter)!;

        foreach (System.Collections.DictionaryEntry de in dict)
        {
            var key = de.Key!;
            if (!key.ToString()!.Contains(tenantId, StringComparison.Ordinal) || !key.ToString()!.Contains(op.ToString(), StringComparison.Ordinal))
            {
                continue;
            }

            var entry = de.Value!;
            var semProp = entry.GetType().GetProperty("Semaphore", BindingFlags.Instance | BindingFlags.Public);
            Assert.NotNull(semProp);
            return (SemaphoreSlim)semProp!.GetValue(entry)!;
        }

        throw new InvalidOperationException("Entry not found.");
    }
}

