namespace Mics.Gateway.Connections;

internal interface IConnectionRegistry
{
    bool TryAdd(ConnectionSession session);
    bool TryRemove(string tenantId, string userId, string deviceId, out ConnectionSession? removed);
    bool TryGet(string tenantId, string userId, string deviceId, out ConnectionSession? session);
    IReadOnlyList<ConnectionSession> GetAllForUser(string tenantId, string userId);
    IReadOnlyList<ConnectionSession> GetAllSessionsSnapshot();
}

internal sealed class ConnectionRegistry : IConnectionRegistry
{
    private readonly Dictionary<(string TenantId, string UserId), Dictionary<string, ConnectionSession>> _map = new();
    private readonly object _lock = new();

    public bool TryAdd(ConnectionSession session)
    {
        lock (_lock)
        {
            var key = (session.TenantId, session.UserId);
            if (!_map.TryGetValue(key, out var byDevice))
            {
                byDevice = new Dictionary<string, ConnectionSession>(StringComparer.Ordinal);
                _map[key] = byDevice;
            }

            if (byDevice.ContainsKey(session.DeviceId))
            {
                return false;
            }

            byDevice[session.DeviceId] = session;
            return true;
        }
    }

    public bool TryRemove(string tenantId, string userId, string deviceId, out ConnectionSession? removed)
    {
        lock (_lock)
        {
            removed = null;
            var key = (tenantId, userId);
            if (!_map.TryGetValue(key, out var byDevice))
            {
                return false;
            }

            if (!byDevice.Remove(deviceId, out removed))
            {
                return false;
            }

            if (byDevice.Count == 0)
            {
                _map.Remove(key);
            }

            return true;
        }
    }

    public bool TryGet(string tenantId, string userId, string deviceId, out ConnectionSession? session)
    {
        lock (_lock)
        {
            session = null;
            return _map.TryGetValue((tenantId, userId), out var byDevice)
                   && byDevice.TryGetValue(deviceId, out session);
        }
    }

    public IReadOnlyList<ConnectionSession> GetAllForUser(string tenantId, string userId)
    {
        lock (_lock)
        {
            return _map.TryGetValue((tenantId, userId), out var byDevice)
                ? byDevice.Values.ToArray()
                : Array.Empty<ConnectionSession>();
        }
    }

    public IReadOnlyList<ConnectionSession> GetAllSessionsSnapshot()
    {
        lock (_lock)
        {
            if (_map.Count == 0)
            {
                return Array.Empty<ConnectionSession>();
            }

            var list = new List<ConnectionSession>();
            foreach (var byDevice in _map.Values)
            {
                list.AddRange(byDevice.Values);
            }
            return list;
        }
    }
}
