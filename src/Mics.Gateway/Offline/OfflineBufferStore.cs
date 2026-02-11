namespace Mics.Gateway.Offline;

using Google.Protobuf;
using Mics.Gateway.Metrics;

internal interface IOfflineBufferStore
{
    bool TryAdd(string tenantId, string userId, ByteString serverFrameBytes, TimeSpan ttl);
    IReadOnlyList<ByteString> Drain(string tenantId, string userId);
}

internal sealed class OfflineBufferStore : IOfflineBufferStore
{
    private sealed record Item(ByteString Bytes, long ExpiresAtMs);
    private sealed class Buffer
    {
        public Queue<Item> Items { get; } = new();
        public long TotalBytes { get; set; }
    }

    private sealed class TenantState
    {
        public Dictionary<string, UserEntry> Users { get; } = new(StringComparer.Ordinal);
        public LinkedList<string> Lru { get; } = new();
    }

    private sealed class UserEntry
    {
        public required Buffer Buffer { get; init; }
        public required LinkedListNode<string> LruNode { get; init; }
    }

    private readonly int _maxMessagesPerUser;
    private readonly int _maxBytesPerUser;

    private readonly int _maxUsersPerTenant;
    private readonly MetricsRegistry? _metrics;

    private readonly Dictionary<string, TenantState> _tenants = new(StringComparer.Ordinal);
    private readonly object _lock = new();

    public OfflineBufferStore(
        int maxMessagesPerUser,
        int maxBytesPerUser,
        int maxUsersPerTenant = 0,
        MetricsRegistry? metrics = null)
    {
        _maxMessagesPerUser = Math.Clamp(maxMessagesPerUser, 0, 1_000_000);
        _maxBytesPerUser = Math.Clamp(maxBytesPerUser, 0, 256 * 1024 * 1024);
        _maxUsersPerTenant = Math.Clamp(maxUsersPerTenant, 0, 1_000_000);
        _metrics = metrics;
    }

    public bool TryAdd(string tenantId, string userId, ByteString serverFrameBytes, TimeSpan ttl)
    {
        if (_maxBytesPerUser > 0 && serverFrameBytes.Length > _maxBytesPerUser)
        {
            return false;
        }

        var expiresAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + (long)ttl.TotalMilliseconds;

        lock (_lock)
        {
            if (!_tenants.TryGetValue(tenantId, out var tenant))
            {
                tenant = new TenantState();
                _tenants[tenantId] = tenant;
            }

            if (!tenant.Users.TryGetValue(userId, out var entry))
            {
                EnsureCapacityForNewUser(tenantId, tenant);
                var node = tenant.Lru.AddLast(userId);
                entry = new UserEntry { Buffer = new Buffer(), LruNode = node };
                tenant.Users[userId] = entry;
            }
            else
            {
                tenant.Lru.Remove(entry.LruNode);
                tenant.Lru.AddLast(entry.LruNode);
            }

            PruneExpired(entry.Buffer);

            if (_maxMessagesPerUser > 0 && entry.Buffer.Items.Count >= _maxMessagesPerUser)
            {
                return false;
            }

            if (_maxBytesPerUser > 0 && entry.Buffer.TotalBytes + serverFrameBytes.Length > _maxBytesPerUser)
            {
                return false;
            }

            entry.Buffer.Items.Enqueue(new Item(serverFrameBytes, expiresAt));
            entry.Buffer.TotalBytes += serverFrameBytes.Length;
            return true;
        }
    }

    public IReadOnlyList<ByteString> Drain(string tenantId, string userId)
    {
        lock (_lock)
        {
            if (!_tenants.TryGetValue(tenantId, out var tenant) || !tenant.Users.TryGetValue(userId, out var entry))
            {
                return Array.Empty<ByteString>();
            }

            if (entry.Buffer.Items.Count == 0)
            {
                RemoveUser(tenantId, tenant, userId);
                return Array.Empty<ByteString>();
            }

            PruneExpired(entry.Buffer);
            if (entry.Buffer.Items.Count == 0)
            {
                RemoveUser(tenantId, tenant, userId);
                return Array.Empty<ByteString>();
            }

            var list = new List<ByteString>(entry.Buffer.Items.Count);
            while (entry.Buffer.Items.Count > 0)
            {
                var item = entry.Buffer.Items.Dequeue();
                entry.Buffer.TotalBytes -= item.Bytes.Length;
                list.Add(item.Bytes);
            }

            RemoveUser(tenantId, tenant, userId);
            return list;
        }
    }

    private void EnsureCapacityForNewUser(string tenantId, TenantState tenant)
    {
        if (_maxUsersPerTenant <= 0)
        {
            return;
        }

        while (tenant.Users.Count >= _maxUsersPerTenant)
        {
            var oldest = tenant.Lru.First;
            if (oldest is null)
            {
                break;
            }

            var oldestUserId = oldest.Value;
            RemoveUser(tenantId, tenant, oldestUserId, evicted: true, removeTenantIfEmpty: false);
        }
    }

    private void RemoveUser(
        string tenantId,
        TenantState tenant,
        string userId,
        bool evicted = false,
        bool removeTenantIfEmpty = true)
    {
        if (!tenant.Users.TryGetValue(userId, out var entry))
        {
            return;
        }

        tenant.Users.Remove(userId);
        tenant.Lru.Remove(entry.LruNode);

        if (evicted)
        {
            _metrics?.CounterInc("mics_offline_buffer_evicted_users_total", 1, ("tenant", tenantId));
        }

        if (removeTenantIfEmpty && tenant.Users.Count == 0)
        {
            _tenants.Remove(tenantId);
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
