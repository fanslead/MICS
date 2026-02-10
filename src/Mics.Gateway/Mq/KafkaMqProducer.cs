using Confluent.Kafka;
using System.Runtime.InteropServices;

namespace Mics.Gateway.Mq;

internal sealed class KafkaMqProducer : IMqProducer, IDisposable
{
    private readonly IProducer<byte[], byte[]> _producer;

    public KafkaMqProducer(IProducer<byte[], byte[]> producer)
    {
        _producer = producer;
    }

    public void Dispose()
    {
        try
        {
            _producer.Flush(TimeSpan.FromSeconds(5));
        }
        catch
        {
        }

        _producer.Dispose();
    }

    public async ValueTask<bool> ProduceAsync(string topic, ReadOnlyMemory<byte> key, ReadOnlyMemory<byte> value, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _producer.ProduceAsync(
                topic,
                new Message<byte[], byte[]>
                {
                    Key = ToWholeArray(key),
                    Value = ToWholeArray(value),
                },
                cancellationToken);

            return result.Status != PersistenceStatus.NotPersisted;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ProduceException<byte[], byte[]>)
        {
            return false;
        }
        catch (KafkaException)
        {
            return false;
        }
    }

    private static byte[] ToWholeArray(ReadOnlyMemory<byte> memory)
    {
        if (MemoryMarshal.TryGetArray(memory, out ArraySegment<byte> seg) &&
            seg.Array is not null &&
            seg.Offset == 0 &&
            seg.Count == seg.Array.Length)
        {
            return seg.Array;
        }

        return memory.ToArray();
    }
}
