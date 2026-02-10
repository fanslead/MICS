using Google.Protobuf;

namespace Mics.LoadTester;

public static class PooledProtobufSerializer
{
    public static PooledProtobufBytes Serialize<T>(T message)
        where T : class, IMessage<T>
    {
        ArgumentNullException.ThrowIfNull(message);

        var size = message.CalculateSize();
        if (size == 0)
        {
            return new PooledProtobufBytes(System.Buffers.ArrayPool<byte>.Shared.Rent(1), 0);
        }

        var buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(size);
        try
        {
            var cos = new CodedOutputStream(buffer);
            message.WriteTo(cos);
            cos.Flush();
            return new PooledProtobufBytes(buffer, size);
        }
        catch
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
            throw;
        }
    }
}
