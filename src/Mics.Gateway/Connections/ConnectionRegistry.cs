namespace Mics.Gateway.Connections;

internal interface IConnectionRegistry
{
    bool TryAdd(ConnectionSession session);
    bool TryRemove(string tenantId, string userId, string deviceId, out ConnectionSession? removed);
    bool TryGet(string tenantId, string userId, string deviceId, out ConnectionSession? session);
    void CopyAllForUserTo(string tenantId, string userId, List<ConnectionSession> destination);
    void CopyAllSessionsTo(List<ConnectionSession> destination);
}

internal sealed class ConnectionRegistry : IConnectionRegistry
{
    private readonly Dictionary<(string TenantId, string UserId), Dictionary<string, ConnectionSession>> _map = new();
    private readonly object _lock = new();
    private int _totalSessions;

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
            _totalSessions++;
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

            if (_totalSessions > 0)
            {
                _totalSessions--;
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

    public void CopyAllForUserTo(string tenantId, string userId, List<ConnectionSession> destination)
    {
        lock (_lock)
        {
            destination.Clear();
            if (!_map.TryGetValue((tenantId, userId), out var byDevice) || byDevice.Count == 0)
            {
                return;
            }

            if (destination.Capacity < byDevice.Count)
            {
                destination.Capacity = byDevice.Count;
            }

            foreach (var s in byDevice.Values)
            {
                destination.Add(s);
            }
        }
    }

    public void CopyAllSessionsTo(List<ConnectionSession> destination)
    {
        lock (_lock)
        {
            destination.Clear();
            if (_map.Count == 0)
            {
                return;
            }

            var total = _totalSessions;

            if (destination.Capacity < total)
            {
                destination.Capacity = total;
            }

            foreach (var byDevice in _map.Values)
            {
                foreach (var s in byDevice.Values)
                {
                    destination.Add(s);
                }
            }
        }
    }
}
