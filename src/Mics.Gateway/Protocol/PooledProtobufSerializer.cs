using System.Buffers;
using Google.Protobuf;

namespace Mics.Gateway.Protocol;

internal sealed class PooledProtobufBytes : IDisposable
{
    private byte[]? _buffer;

    public PooledProtobufBytes(byte[] buffer, int length)
    {
        _buffer = buffer;
        Length = length;
    }

    public byte[] Buffer => _buffer ?? Array.Empty<byte>();

    public int Length { get; }

    public ReadOnlyMemory<byte> Memory => _buffer is null ? ReadOnlyMemory<byte>.Empty : _buffer.AsMemory(0, Length);

    public void Dispose()
    {
        var buffer = Interlocked.Exchange(ref _buffer, null);
        if (buffer is not null)
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}

internal static class PooledProtobufSerializer
{
    public static PooledProtobufBytes Serialize(IMessage message)
    {
        var size = message.CalculateSize();
        if (size == 0)
        {
            return new PooledProtobufBytes(ArrayPool<byte>.Shared.Rent(1), 0);
        }

        var buffer = ArrayPool<byte>.Shared.Rent(size);
        var cos = new CodedOutputStream(buffer);
        message.WriteTo(cos);
        cos.Flush();
        return new PooledProtobufBytes(buffer, size);
    }
}
