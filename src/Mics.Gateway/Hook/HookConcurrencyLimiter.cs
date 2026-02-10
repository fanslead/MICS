using System.Collections.Concurrent;
using Mics.Gateway.Metrics;

namespace Mics.Gateway.Hook;

internal interface IHookConcurrencyLimiter
{
    ValueTask<IAsyncDisposable?> TryAcquireAsync(string tenantId, HookOperation op, HookAcquirePolicy policy, CancellationToken cancellationToken);
}

internal sealed class HookConcurrencyLimiter : IHookConcurrencyLimiter
{
    private sealed class Entry
    {
        private int _reservedPermits;
        private int _desiredMax;

        public Entry(int baseMaxConcurrency)
        {
            BaseMaxConcurrency = baseMaxConcurrency;
            _desiredMax = baseMaxConcurrency;
            Semaphore = new SemaphoreSlim(baseMaxConcurrency, baseMaxConcurrency);
        }

        public int BaseMaxConcurrency { get; }
        public SemaphoreSlim Semaphore { get; }

        public void AdjustMax(int desiredMaxConcurrency)
        {
            desiredMaxConcurrency = Math.Clamp(desiredMaxConcurrency, 1, BaseMaxConcurrency);
            Volatile.Write(ref _desiredMax, desiredMaxConcurrency);

            var reserveTarget = BaseMaxConcurrency - desiredMaxConcurrency;

            // Increase reserved permits (reduce concurrency) by acquiring permits and holding them.
            while (Volatile.Read(ref _reservedPermits) < reserveTarget)
            {
                if (!Semaphore.Wait(0))
                {
                    break;
                }

                Interlocked.Increment(ref _reservedPermits);
            }

            // Decrease reserved permits (increase concurrency) by releasing held permits.
            while (Volatile.Read(ref _reservedPermits) > reserveTarget)
            {
                Semaphore.Release();
                Interlocked.Decrement(ref _reservedPermits);
            }
        }
    }

    private sealed class Releaser : IAsyncDisposable
    {
        private SemaphoreSlim? _semaphore;

        public Releaser(SemaphoreSlim semaphore)
        {
            _semaphore = semaphore;
        }

        public ValueTask DisposeAsync()
        {
            var sem = Interlocked.Exchange(ref _semaphore, null);
            sem?.Release();
            return ValueTask.CompletedTask;
        }
    }

    private readonly ConcurrentDictionary<(string TenantId, HookOperation Op), Entry> _entries = new();
    private readonly MetricsRegistry _metrics;

    public HookConcurrencyLimiter(MetricsRegistry metrics)
    {
        _metrics = metrics;
    }

    public async ValueTask<IAsyncDisposable?> TryAcquireAsync(string tenantId, HookOperation op, HookAcquirePolicy policy, CancellationToken cancellationToken)
    {
        var max = Math.Max(1, policy.MaxConcurrency);
        var key = (tenantId, op);
        var entry = GetOrUpdateEntry(key, max);
        entry.AdjustMax(max);

        var queueTimeout = policy.QueueTimeout < TimeSpan.Zero ? TimeSpan.Zero : policy.QueueTimeout;
        var ok = await entry.Semaphore.WaitAsync(queueTimeout, cancellationToken);
        if (!ok)
        {
            _metrics.CounterInc("mics_hook_limiter_rejected_total", 1, ("tenant", tenantId), ("op", op.ToString()), ("reason", "queue_timeout"));
            return null;
        }

        return new Releaser(entry.Semaphore);
    }

    private Entry GetOrUpdateEntry((string TenantId, HookOperation Op) key, int maxConcurrency)
    {
        while (true)
        {
            var existing = _entries.GetOrAdd(key, _ => new Entry(maxConcurrency));
            if (existing.BaseMaxConcurrency >= maxConcurrency)
            {
                return existing;
            }

            var updated = new Entry(maxConcurrency);
            if (_entries.TryUpdate(key, updated, existing))
            {
                return updated;
            }
        }
    }
}
