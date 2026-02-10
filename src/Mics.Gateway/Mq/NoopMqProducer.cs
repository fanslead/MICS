namespace Mics.Gateway.Mq;

internal sealed class NoopMqProducer : IMqProducer
{
    public ValueTask<bool> ProduceAsync(string topic, ReadOnlyMemory<byte> key, ReadOnlyMemory<byte> value, CancellationToken cancellationToken) =>
        ValueTask.FromResult(true);
}

