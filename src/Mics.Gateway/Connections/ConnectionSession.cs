using System.Net.WebSockets;
using Mics.Contracts.Hook.V1;

namespace Mics.Gateway.Connections;

internal sealed class ConnectionSession
{
    private long _lastSeenUnixMs;
    private long _lastLeaseRenewUnixMs;

    public ConnectionSession(
        string tenantId,
        string userId,
        string deviceId,
        string connectionId,
        string traceId,
        WebSocket socket,
        TenantRuntimeConfig tenantConfig)
    {
        TenantId = tenantId;
        UserId = userId;
        DeviceId = deviceId;
        ConnectionId = connectionId;
        TraceId = traceId;
        Socket = socket;
        TenantConfig = tenantConfig;
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _lastSeenUnixMs = now;
        _lastLeaseRenewUnixMs = now;
    }

    public string TenantId { get; }
    public string UserId { get; }
    public string DeviceId { get; }
    public string ConnectionId { get; }
    public string TraceId { get; }
    public WebSocket Socket { get; }
    public TenantRuntimeConfig TenantConfig { get; }

    public long LastSeenUnixMs => Volatile.Read(ref _lastSeenUnixMs);

    public long LastLeaseRenewUnixMs => Volatile.Read(ref _lastLeaseRenewUnixMs);

    public void Touch(long unixMs)
    {
        Interlocked.Exchange(ref _lastSeenUnixMs, unixMs);
    }

    public void Touch()
    {
        Interlocked.Exchange(ref _lastSeenUnixMs, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    public void MarkLeaseRenewed(long unixMs)
    {
        Interlocked.Exchange(ref _lastLeaseRenewUnixMs, unixMs);
    }
}
