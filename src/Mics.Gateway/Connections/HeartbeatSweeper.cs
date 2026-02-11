using System.Net.WebSockets;
using Microsoft.Extensions.Logging;
using Mics.Gateway.Metrics;
using Mics.Gateway.Protocol;
using Mics.Gateway.Infrastructure.Redis;

namespace Mics.Gateway.Connections;

internal sealed class HeartbeatSweeper
{
    private const long SnapshotRefreshIntervalMs = 5_000;
    private const int TargetFullSweepSeconds = 5;
    private const int MinBatchSize = 1_024;

    private readonly IConnectionRegistry _connections;
    private readonly IConnectionAdmission _admission;
    private readonly MetricsRegistry _metrics;
    private readonly ILogger<HeartbeatSweeper> _logger;
    private readonly TimeProvider _timeProvider;

    private readonly List<ConnectionSession> _snapshot = new();
    private long _snapshotAtUnixMs;
    private int _cursor;

    public HeartbeatSweeper(
        IConnectionRegistry connections,
        IConnectionAdmission admission,
        MetricsRegistry metrics,
        ILogger<HeartbeatSweeper> logger,
        TimeProvider timeProvider)
    {
        _connections = connections;
        _admission = admission;
        _metrics = metrics;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    public async ValueTask SweepOnceAsync(CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
        _connections.CopyAllSessionsTo(_snapshot);
        if (_snapshot.Count == 0)
        {
            return;
        }

        foreach (var s in _snapshot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ProcessSessionAsync(s, now, cancellationToken);
        }
    }

    public async ValueTask SweepTickAsync(CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
        if (_snapshotAtUnixMs == 0 || now - _snapshotAtUnixMs >= SnapshotRefreshIntervalMs)
        {
            _connections.CopyAllSessionsTo(_snapshot);
            _snapshotAtUnixMs = now;
            if (_cursor >= _snapshot.Count)
            {
                _cursor = 0;
            }
        }

        if (_snapshot.Count == 0)
        {
            return;
        }

        var batch = Math.Min(
            _snapshot.Count,
            Math.Max(MinBatchSize, (_snapshot.Count + TargetFullSweepSeconds - 1) / TargetFullSweepSeconds));
        for (var i = 0; i < batch && _snapshot.Count > 0; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_cursor >= _snapshot.Count)
            {
                _cursor = 0;
            }

            var s = _snapshot[_cursor++];
            await ProcessSessionAsync(s, now, cancellationToken);
        }
    }

    private async ValueTask ProcessSessionAsync(ConnectionSession s, long now, CancellationToken cancellationToken)
    {
        var timeoutSec = s.TenantConfig.HeartbeatTimeoutSeconds > 0 ? s.TenantConfig.HeartbeatTimeoutSeconds : 30;
        var lastSeen = s.LastSeenUnixMs;
        if (now - lastSeen <= timeoutSec * 1000L)
        {
            if (s.Socket.State == WebSocketState.Open)
            {
                var renewEveryMs = Math.Max(1_000, (timeoutSec * 1000L) / 2);
                if (now - s.LastLeaseRenewUnixMs < renewEveryMs)
                {
                    return;
                }

                try
                {
                    await _admission.RenewLeaseAsync(s.TenantId, s.UserId, s.DeviceId, timeoutSec, cancellationToken);
                    s.MarkLeaseRenewed(now);
                }
                catch
                {
                    _metrics.CounterInc("mics_ws_lease_renew_fail_total", 1, ("tenant", s.TenantId));
                }
            }

            return;
        }

        if (s.Socket.State != WebSocketState.Open)
        {
            return;
        }

        _metrics.CounterInc("mics_ws_heartbeat_timeouts_total", 1, ("tenant", s.TenantId));
        _logger.LogWarning("ws_heartbeat_timeout tenant={TenantId} user={UserId} device={DeviceId}", s.TenantId, s.UserId, s.DeviceId);

        try
        {
            await s.Socket.CloseAsync((WebSocketCloseStatus)MicsProtocolCodes.CloseHeartbeatTimeout, "heartbeat timeout", cancellationToken);
        }
        catch
        {
        }
    }
}
