using System.Collections.Concurrent;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using Google.Protobuf;
using Mics.Contracts.Hook.V1;
using Mics.Gateway.Metrics;

namespace Mics.Gateway.Mq;

internal sealed class MqEventDispatcher
{
    private sealed class TenantPending
    {
        public int Pending;
    }

    private sealed record DlqFallbackItem(string TenantId, EventType EventType, byte[] Key, byte[] Value, int Attempt);

    private readonly IMqProducer _producer;
    private readonly MetricsRegistry _metrics;
    private readonly TimeProvider _timeProvider;
    private readonly MqEventDispatcherOptions _options;
    private readonly Channel<MqEvent> _channel;
    private readonly ConcurrentDictionary<string, TenantPending> _tenantPending = new(StringComparer.Ordinal);

    private readonly Channel<DlqFallbackItem>? _dlqFallback;
    private readonly ConcurrentDictionary<string, TenantPending> _dlqFallbackTenantPending = new(StringComparer.Ordinal);

    public MqEventDispatcher(
        IMqProducer producer,
        MetricsRegistry metrics,
        TimeProvider timeProvider,
        MqEventDispatcherOptions options)
    {
        _producer = producer;
        _metrics = metrics;
        _timeProvider = timeProvider;
        _options = options;

        var capacity = options.QueueCapacity > 0 ? options.QueueCapacity : 50_000;
        _channel = Channel.CreateBounded<MqEvent>(new BoundedChannelOptions(capacity)
        {
            AllowSynchronousContinuations = true,
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropWrite,
        });

        var dlqFallbackCapacity = options.DlqFallbackQueueCapacity;
        _dlqFallback = dlqFallbackCapacity > 0
            ? Channel.CreateBounded<DlqFallbackItem>(new BoundedChannelOptions(dlqFallbackCapacity)
            {
                AllowSynchronousContinuations = true,
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.DropWrite,
            })
            : null;
    }

    public bool TryEnqueue(MqEvent evt)
    {
        var tenantId = evt.TenantId ?? "";
        var maxPending = _options.MaxPendingPerTenant > 0 ? _options.MaxPendingPerTenant : _options.QueueCapacity;
        var pending = _tenantPending.GetOrAdd(tenantId, _ => new TenantPending());

        if (Interlocked.Increment(ref pending.Pending) > maxPending)
        {
            Interlocked.Decrement(ref pending.Pending);
            _metrics.CounterInc("mics_mq_dropped_total", 1, ("tenant", tenantId), ("reason", "tenant_quota"));
            return false;
        }

        var ok = _channel.Writer.TryWrite(evt);
        if (!ok)
        {
            Interlocked.Decrement(ref pending.Pending);
            _metrics.CounterInc("mics_mq_dropped_total", 1, ("tenant", tenantId), ("reason", "queue_full"));
        }

        return ok;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (_dlqFallback is null)
        {
            await RunPrimaryAsync(cancellationToken);
            return;
        }

        await Task.WhenAll(RunPrimaryAsync(cancellationToken), RunDlqFallbackAsync(cancellationToken));
    }

    private async Task RunPrimaryAsync(CancellationToken cancellationToken)
    {
        while (await _channel.Reader.WaitToReadAsync(cancellationToken))
        {
            while (_channel.Reader.TryRead(out var evt))
            {
                try
                {
                    await PublishWithRetryAsync(evt, cancellationToken);
                }
                finally
                {
                    ReleasePending(evt.TenantId ?? "");
                }
            }
        }
    }

    private async Task RunDlqFallbackAsync(CancellationToken cancellationToken)
    {
        var ch = _dlqFallback;
        if (ch is null)
        {
            return;
        }

        while (await ch.Reader.WaitToReadAsync(cancellationToken))
        {
            while (ch.Reader.TryRead(out var item))
            {
                try
                {
                    await PublishDlqFallbackAsync(item, cancellationToken);
                }
                finally
                {
                    ReleaseDlqFallbackPending(item.TenantId);
                }
            }
        }
    }

    private void ReleasePending(string tenantId)
    {
        if (!_tenantPending.TryGetValue(tenantId, out var pending))
        {
            return;
        }

        var value = Interlocked.Decrement(ref pending.Pending);
        if (value <= 0)
        {
            // Best-effort cleanup; avoid unbounded dictionary growth for inactive tenants.
            _tenantPending.TryRemove(new KeyValuePair<string, TenantPending>(tenantId, pending));
        }
    }

    private void ReleaseDlqFallbackPending(string tenantId)
    {
        if (!_dlqFallbackTenantPending.TryGetValue(tenantId, out var pending))
        {
            return;
        }

        var value = Interlocked.Decrement(ref pending.Pending);
        if (value <= 0)
        {
            _dlqFallbackTenantPending.TryRemove(new KeyValuePair<string, TenantPending>(tenantId, pending));
        }
    }

    private async ValueTask PublishWithRetryAsync(MqEvent evt, CancellationToken cancellationToken)
    {
        var maxAttempts = Math.Clamp(_options.MaxAttempts, 1, 10);
        var tenantId = evt.TenantId ?? "";
        var topic = MqTopicName.GetEventTopic(tenantId);
        var dlqTopic = MqTopicName.GetDlqTopic(tenantId);

        var key = Encoding.UTF8.GetBytes(evt.UserId ?? "");
        var value = evt.ToByteArray();

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var ok = await _producer.ProduceAsync(topic, key, value, cancellationToken);
            if (ok)
            {
                _metrics.CounterInc("mics_mq_published_total", 1, ("tenant", tenantId), ("topic", "event"), ("event_type", evt.EventType.ToString()));
                return;
            }

            _metrics.CounterInc("mics_mq_failed_total", 1, ("tenant", tenantId), ("topic", "event"), ("event_type", evt.EventType.ToString()));
            if (attempt == maxAttempts)
            {
                break;
            }

            _metrics.CounterInc("mics_mq_retried_total", 1, ("tenant", tenantId), ("event_type", evt.EventType.ToString()));
            var delay = ComputeBackoff(attempt);
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken);
            }
        }

        var dlqOk = await _producer.ProduceAsync(dlqTopic, key, value, cancellationToken);
        if (dlqOk)
        {
            _metrics.CounterInc("mics_mq_published_total", 1, ("tenant", tenantId), ("topic", "dlq"), ("event_type", evt.EventType.ToString()));
            _metrics.CounterInc("mics_mq_dlq_total", 1, ("tenant", tenantId), ("event_type", evt.EventType.ToString()));
            return;
        }

        if (TryEnqueueDlqFallback(tenantId, evt.EventType, key, value))
        {
            return;
        }

        _metrics.CounterInc("mics_mq_dropped_total", 1, ("tenant", tenantId), ("reason", "dlq_failed"));
    }

    private bool TryEnqueueDlqFallback(string tenantId, EventType eventType, byte[] key, byte[] value)
    {
        var ch = _dlqFallback;
        if (ch is null)
        {
            return false;
        }

        var maxPending = _options.DlqFallbackMaxPendingPerTenant;
        if (maxPending <= 0)
        {
            maxPending = 1_000;
        }

        var pending = _dlqFallbackTenantPending.GetOrAdd(tenantId, _ => new TenantPending());
        if (Interlocked.Increment(ref pending.Pending) > maxPending)
        {
            Interlocked.Decrement(ref pending.Pending);
            _metrics.CounterInc("mics_mq_dlq_fallback_dropped_total", 1, ("tenant", tenantId), ("reason", "tenant_quota"));
            return false;
        }

        var ok = ch.Writer.TryWrite(new DlqFallbackItem(tenantId, eventType, key, value, Attempt: 1));
        if (!ok)
        {
            Interlocked.Decrement(ref pending.Pending);
            _metrics.CounterInc("mics_mq_dlq_fallback_dropped_total", 1, ("tenant", tenantId), ("reason", "queue_full"));
            return false;
        }

        _metrics.CounterInc("mics_mq_dlq_fallback_enqueued_total", 1, ("tenant", tenantId), ("event_type", eventType.ToString()));
        return true;
    }

    private async ValueTask PublishDlqFallbackAsync(DlqFallbackItem item, CancellationToken cancellationToken)
    {
        var maxAttempts = Math.Max(1, _options.DlqFallbackMaxAttempts);
        var attempt = item.Attempt;
        var dlqTopic = MqTopicName.GetDlqTopic(item.TenantId);

        while (attempt <= maxAttempts)
        {
            var ok = await _producer.ProduceAsync(dlqTopic, item.Key, item.Value, cancellationToken);
            if (ok)
            {
                _metrics.CounterInc("mics_mq_published_total", 1, ("tenant", item.TenantId), ("topic", "dlq"), ("event_type", item.EventType.ToString()));
                _metrics.CounterInc("mics_mq_dlq_total", 1, ("tenant", item.TenantId), ("event_type", item.EventType.ToString()));
                _metrics.CounterInc("mics_mq_dlq_fallback_published_total", 1, ("tenant", item.TenantId), ("event_type", item.EventType.ToString()));
                return;
            }

            _metrics.CounterInc("mics_mq_dlq_fallback_failed_total", 1, ("tenant", item.TenantId), ("event_type", item.EventType.ToString()));
            if (attempt >= maxAttempts)
            {
                break;
            }

            _metrics.CounterInc("mics_mq_dlq_fallback_retried_total", 1, ("tenant", item.TenantId), ("event_type", item.EventType.ToString()));
            var delay = ComputeBackoff(_options.DlqFallbackRetryBackoffBase, attempt);
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken);
            }

            attempt++;
        }

        _metrics.CounterInc("mics_mq_dropped_total", 1, ("tenant", item.TenantId), ("reason", "dlq_fallback_giveup"));
    }

    private TimeSpan ComputeBackoff(int attempt)
    {
        return ComputeBackoff(_options.RetryBackoffBase, attempt);
    }

    private static TimeSpan ComputeBackoff(TimeSpan baseDelay, int attempt)
    {
        if (baseDelay <= TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        var baseMs = baseDelay.TotalMilliseconds;
        var factor = 1L << Math.Min(attempt - 1, 10); // cap at 1024x
        var ms = Math.Min(baseMs * factor, 5_000);
        return TimeSpan.FromMilliseconds(ms);
    }
}
