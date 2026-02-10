using System.Buffers;

namespace Mics.LoadTester;

public struct PooledProtobufBytes : IDisposable
{
    private byte[]? _buffer;
    private int _length;

    public byte[] Buffer => _buffer ?? Array.Empty<byte>();
    public int Length => _buffer is null ? 0 : _length;

    public ReadOnlyMemory<byte> Memory => Buffer.AsMemory(0, Length);

    public PooledProtobufBytes(byte[] buffer, int length)
    {
        _buffer = buffer;
        _length = length;
    }

    public void Dispose()
    {
        var buffer = Interlocked.Exchange(ref _buffer, null);
        if (buffer is not null)
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        _length = 0;
    }
}
