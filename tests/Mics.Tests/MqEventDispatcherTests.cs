using Mics.Contracts.Hook.V1;
using Mics.Gateway.Metrics;
using Mics.Gateway.Mq;

namespace Mics.Tests;

public sealed class MqEventDispatcherTests
{
    private sealed class SequenceProducer : IMqProducer
    {
        private readonly bool[] _results;
        private int _idx;

        public SequenceProducer(params bool[] results)
        {
            _results = results.Length == 0 ? new[] { true } : results;
        }

        public int EventTopicCalls { get; private set; }
        public int DlqTopicCalls { get; private set; }

        public ValueTask<bool> ProduceAsync(string topic, ReadOnlyMemory<byte> key, ReadOnlyMemory<byte> value, CancellationToken cancellationToken)
        {
            if (topic.EndsWith("-event-dlq", StringComparison.Ordinal))
            {
                DlqTopicCalls++;
            }
            else
            {
                EventTopicCalls++;
            }

            var i = Interlocked.Increment(ref _idx) - 1;
            var ok = i < _results.Length ? _results[i] : _results[^1];
            return ValueTask.FromResult(ok);
        }
    }

    [Fact]
    public async Task RunAsync_RetriesUntilSuccess()
    {
        var producer = new SequenceProducer(false, false, true);
        var metrics = new MetricsRegistry();
        var dispatcher = new MqEventDispatcher(
            producer,
            metrics,
            TimeProvider.System,
            new MqEventDispatcherOptions(
                QueueCapacity: 16,
                MaxPendingPerTenant: 16,
                MaxAttempts: 3,
                RetryBackoffBase: TimeSpan.Zero,
                IdleDelay: TimeSpan.FromMilliseconds(1),
                DlqFallbackQueueCapacity: 0));

        var ok = dispatcher.TryEnqueue(new MqEvent
        {
            TenantId = "t1",
            EventType = EventType.SingleChatMsg,
            MsgId = "m1",
            UserId = "u1",
            DeviceId = "d1",
            ToUserId = "u2",
            GroupId = "",
            EventData = Google.Protobuf.ByteString.CopyFrom(Array.Empty<byte>()),
            Timestamp = 1,
            NodeId = "n1",
        });
        Assert.True(ok);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var run = dispatcher.RunAsync(cts.Token);
        await Task.Delay(50, CancellationToken.None);
        cts.Cancel();
        try { await run; } catch (OperationCanceledException) { }

        Assert.Equal(3, producer.EventTopicCalls);
        Assert.Equal(0, producer.DlqTopicCalls);
    }

    [Fact]
    public async Task RunAsync_SendsDlqAfterMaxAttempts()
    {
        var producer = new SequenceProducer(false, false, false, false);
        var metrics = new MetricsRegistry();
        var dispatcher = new MqEventDispatcher(
            producer,
            metrics,
            TimeProvider.System,
            new MqEventDispatcherOptions(
                QueueCapacity: 16,
                MaxPendingPerTenant: 16,
                MaxAttempts: 2,
                RetryBackoffBase: TimeSpan.Zero,
                IdleDelay: TimeSpan.FromMilliseconds(1),
                DlqFallbackQueueCapacity: 0));

        dispatcher.TryEnqueue(new MqEvent
        {
            TenantId = "t1",
            EventType = EventType.ConnectOnline,
            MsgId = "",
            UserId = "u1",
            DeviceId = "d1",
            ToUserId = "",
            GroupId = "",
            EventData = Google.Protobuf.ByteString.CopyFrom(new byte[] { 1 }),
            Timestamp = 1,
            NodeId = "n1",
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var run = dispatcher.RunAsync(cts.Token);
        await Task.Delay(50, CancellationToken.None);
        cts.Cancel();
        try { await run; } catch (OperationCanceledException) { }

        Assert.Equal(2, producer.EventTopicCalls);
        Assert.Equal(1, producer.DlqTopicCalls);
    }

    [Fact]
    public async Task RunAsync_DlqFailure_IsBuffered_AndRetriedInMemory()
    {
        var dlqCalls = 0;
        var producer = new DelegateProducer((topic, _key, _value) =>
        {
            if (!topic.EndsWith("-event-dlq", StringComparison.Ordinal))
            {
                return false; // event topic always fails
            }

            // DLQ: fail first, then succeed.
            return Interlocked.Increment(ref dlqCalls) >= 2;
        });

        var metrics = new MetricsRegistry();
        var dispatcher = new MqEventDispatcher(
            producer,
            metrics,
            TimeProvider.System,
            new MqEventDispatcherOptions(
                QueueCapacity: 16,
                MaxPendingPerTenant: 16,
                MaxAttempts: 1,
                RetryBackoffBase: TimeSpan.Zero,
                IdleDelay: TimeSpan.FromMilliseconds(1),
                DlqFallbackQueueCapacity: 16,
                DlqFallbackMaxPendingPerTenant: 16,
                DlqFallbackMaxAttempts: 3,
                DlqFallbackRetryBackoffBase: TimeSpan.Zero));

        dispatcher.TryEnqueue(new MqEvent
        {
            TenantId = "t1",
            EventType = EventType.SingleChatMsg,
            MsgId = "m1",
            UserId = "u1",
            DeviceId = "d1",
            ToUserId = "u2",
            GroupId = "",
            EventData = Google.Protobuf.ByteString.CopyFrom(Array.Empty<byte>()),
            Timestamp = 1,
            NodeId = "n1",
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var run = dispatcher.RunAsync(cts.Token);
        await Task.Delay(120, CancellationToken.None);
        cts.Cancel();
        try { await run; } catch (OperationCanceledException) { }

        Assert.True(dlqCalls >= 2);

        var prom = metrics.CollectPrometheusText();
        Assert.Contains("mics_mq_dlq_fallback_enqueued_total{tenant=\"t1\"", prom);
        Assert.Contains("mics_mq_dlq_fallback_published_total{tenant=\"t1\"", prom);
    }

    private sealed class DelegateProducer : IMqProducer
    {
        private readonly Func<string, ReadOnlyMemory<byte>, ReadOnlyMemory<byte>, bool> _handler;

        public DelegateProducer(Func<string, ReadOnlyMemory<byte>, ReadOnlyMemory<byte>, bool> handler)
        {
            _handler = handler;
        }

        public ValueTask<bool> ProduceAsync(string topic, ReadOnlyMemory<byte> key, ReadOnlyMemory<byte> value, CancellationToken cancellationToken) =>
            ValueTask.FromResult(_handler(topic, key, value));
    }
}
