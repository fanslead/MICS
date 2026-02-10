namespace Mics.Gateway.Mq;

internal interface IMqProducer
{
    ValueTask<bool> ProduceAsync(string topic, ReadOnlyMemory<byte> key, ReadOnlyMemory<byte> value, CancellationToken cancellationToken);
}

