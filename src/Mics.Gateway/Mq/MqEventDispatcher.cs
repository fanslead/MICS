using System.Text;
using System.Threading.Channels;
using Google.Protobuf;
using Mics.Contracts.Hook.V1;
using Mics.Gateway.Metrics;

namespace Mics.Gateway.Mq;

internal sealed class MqEventDispatcher
{
    private readonly IMqProducer _producer;
    private readonly MetricsRegistry _metrics;
    private readonly TimeProvider _timeProvider;
    private readonly MqEventDispatcherOptions _options;
    private readonly Channel<MqEvent> _channel;

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
    }

    public bool TryEnqueue(MqEvent evt)
    {
        var ok = _channel.Writer.TryWrite(evt);
        if (!ok)
        {
            _metrics.CounterInc("mics_mq_dropped_total", 1, ("tenant", evt.TenantId), ("reason", "queue_full"));
        }

        return ok;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (await _channel.Reader.WaitToReadAsync(cancellationToken))
        {
            while (_channel.Reader.TryRead(out var evt))
            {
                await PublishWithRetryAsync(evt, cancellationToken);
            }
        }
    }

    private async ValueTask PublishWithRetryAsync(MqEvent evt, CancellationToken cancellationToken)
    {
        var maxAttempts = Math.Clamp(_options.MaxAttempts, 1, 10);
        var topic = MqTopicName.GetEventTopic(evt.TenantId);
        var dlqTopic = MqTopicName.GetDlqTopic(evt.TenantId);

        var key = Encoding.UTF8.GetBytes(evt.UserId ?? "");
        var value = evt.ToByteArray();

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var ok = await _producer.ProduceAsync(topic, key, value, cancellationToken);
            if (ok)
            {
                _metrics.CounterInc("mics_mq_published_total", 1, ("tenant", evt.TenantId), ("topic", "event"), ("event_type", evt.EventType.ToString()));
                return;
            }

            _metrics.CounterInc("mics_mq_failed_total", 1, ("tenant", evt.TenantId), ("topic", "event"), ("event_type", evt.EventType.ToString()));
            if (attempt == maxAttempts)
            {
                break;
            }

            _metrics.CounterInc("mics_mq_retried_total", 1, ("tenant", evt.TenantId), ("event_type", evt.EventType.ToString()));
            var delay = ComputeBackoff(attempt);
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken);
            }
        }

        var dlqOk = await _producer.ProduceAsync(dlqTopic, key, value, cancellationToken);
        if (dlqOk)
        {
            _metrics.CounterInc("mics_mq_published_total", 1, ("tenant", evt.TenantId), ("topic", "dlq"), ("event_type", evt.EventType.ToString()));
            _metrics.CounterInc("mics_mq_dlq_total", 1, ("tenant", evt.TenantId), ("event_type", evt.EventType.ToString()));
            return;
        }

        _metrics.CounterInc("mics_mq_dropped_total", 1, ("tenant", evt.TenantId), ("reason", "dlq_failed"));
    }

    private TimeSpan ComputeBackoff(int attempt)
    {
        var baseDelay = _options.RetryBackoffBase;
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
