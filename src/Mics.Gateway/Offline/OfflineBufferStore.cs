namespace Mics.Gateway.Offline;

internal interface IOfflineBufferStore
{
    bool TryAdd(string tenantId, string userId, byte[] serverFrameBytes, TimeSpan ttl);
    IReadOnlyList<byte[]> Drain(string tenantId, string userId);
}

internal sealed class OfflineBufferStore : IOfflineBufferStore
{
    private sealed record Item(byte[] Bytes, long ExpiresAtMs);
    private sealed class Buffer
    {
        public Queue<Item> Items { get; } = new();
        public long TotalBytes { get; set; }
    }

    private readonly int _maxMessagesPerUser;
    private readonly int _maxBytesPerUser;

    private readonly Dictionary<(string TenantId, string UserId), Buffer> _buffers = new();
    private readonly object _lock = new();

    public OfflineBufferStore(int maxMessagesPerUser, int maxBytesPerUser)
    {
        _maxMessagesPerUser = Math.Clamp(maxMessagesPerUser, 0, 1_000_000);
        _maxBytesPerUser = Math.Clamp(maxBytesPerUser, 0, 256 * 1024 * 1024);
    }

    public bool TryAdd(string tenantId, string userId, byte[] serverFrameBytes, TimeSpan ttl)
    {
        if (_maxBytesPerUser > 0 && serverFrameBytes.Length > _maxBytesPerUser)
        {
            return false;
        }

        var expiresAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + (long)ttl.TotalMilliseconds;

        lock (_lock)
        {
            var key = (tenantId, userId);
            if (!_buffers.TryGetValue(key, out var buffer))
            {
                buffer = new Buffer();
                _buffers[key] = buffer;
            }

            PruneExpired(buffer);

            if (_maxMessagesPerUser > 0 && buffer.Items.Count >= _maxMessagesPerUser)
            {
                return false;
            }

            if (_maxBytesPerUser > 0 && buffer.TotalBytes + serverFrameBytes.Length > _maxBytesPerUser)
            {
                return false;
            }

            buffer.Items.Enqueue(new Item(serverFrameBytes, expiresAt));
            buffer.TotalBytes += serverFrameBytes.Length;
            return true;
        }
    }

    public IReadOnlyList<byte[]> Drain(string tenantId, string userId)
    {
        lock (_lock)
        {
            var key = (tenantId, userId);
            if (!_buffers.TryGetValue(key, out var buffer) || buffer.Items.Count == 0)
            {
                return Array.Empty<byte[]>();
            }

            PruneExpired(buffer);
            if (buffer.Items.Count == 0)
            {
                _buffers.Remove(key);
                return Array.Empty<byte[]>();
            }

            var list = new List<byte[]>(buffer.Items.Count);
            while (buffer.Items.Count > 0)
            {
                var item = buffer.Items.Dequeue();
                buffer.TotalBytes -= item.Bytes.Length;
                list.Add(item.Bytes);
            }

            _buffers.Remove(key);
            return list;
        }
    }

    private static void PruneExpired(Buffer buffer)
    {
        if (buffer.Items.Count == 0)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        while (buffer.Items.Count > 0)
        {
            var head = buffer.Items.Peek();
            if (head.ExpiresAtMs >= now)
            {
                break;
            }

            buffer.Items.Dequeue();
            buffer.TotalBytes -= head.Bytes.Length;
        }
    }
}
