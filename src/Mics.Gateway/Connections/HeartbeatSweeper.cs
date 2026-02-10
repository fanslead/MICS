using System.Net.WebSockets;
using Microsoft.Extensions.Logging;
using Mics.Gateway.Metrics;
using Mics.Gateway.Protocol;
using Mics.Gateway.Infrastructure.Redis;

namespace Mics.Gateway.Connections;

internal sealed class HeartbeatSweeper
{
    private readonly IConnectionRegistry _connections;
    private readonly IConnectionAdmission _admission;
    private readonly MetricsRegistry _metrics;
    private readonly ILogger<HeartbeatSweeper> _logger;
    private readonly TimeProvider _timeProvider;

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
        var sessions = _connections.GetAllSessionsSnapshot();
        if (sessions.Count == 0)
        {
            return;
        }

        foreach (var s in sessions)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var timeoutSec = s.TenantConfig.HeartbeatTimeoutSeconds > 0 ? s.TenantConfig.HeartbeatTimeoutSeconds : 30;
            var lastSeen = s.LastSeenUnixMs;
            if (now - lastSeen <= timeoutSec * 1000L)
            {
                if (s.Socket.State == WebSocketState.Open)
                {
                    var renewEveryMs = Math.Max(1_000, (timeoutSec * 1000L) / 2);
                    if (now - s.LastLeaseRenewUnixMs < renewEveryMs)
                    {
                        continue;
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

                continue;
            }

            if (s.Socket.State != WebSocketState.Open)
            {
                continue;
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
}
