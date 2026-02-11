using System.Net.WebSockets;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Mics.Gateway.Connections;
using Mics.Gateway.Infrastructure.Redis;
using Mics.Gateway.Infrastructure.Pooling;
using Mics.Gateway.Metrics;
using Mics.Gateway.Protocol;

namespace Mics.Gateway.Infrastructure;

internal sealed class ShutdownDrainService : IHostedService
{
    private readonly string _nodeId;
    private readonly TimeSpan _drainTimeout;
    private readonly IShutdownState _shutdown;
    private readonly IConnectionRegistry _connections;
    private readonly IConnectionAdmission _admission;
    private readonly MetricsRegistry _metrics;
    private readonly ILogger<ShutdownDrainService> _logger;

    public ShutdownDrainService(
        string nodeId,
        TimeSpan drainTimeout,
        IShutdownState shutdown,
        IConnectionRegistry connections,
        IConnectionAdmission admission,
        MetricsRegistry metrics,
        ILogger<ShutdownDrainService> logger)
    {
        _nodeId = nodeId;
        _drainTimeout = drainTimeout;
        _shutdown = shutdown;
        _connections = connections;
        _admission = admission;
        _metrics = metrics;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _shutdown.BeginDrain();

        using var lease = ConnectionSessionListPool.Rent();
        var sessions = lease.List;
        _connections.CopyAllSessionsTo(sessions);
        if (sessions.Count == 0)
        {
            return;
        }

        _logger.LogWarning("shutdown_drain_begin node={NodeId} sessions={Count}", _nodeId, sessions.Count);

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (_drainTimeout > TimeSpan.Zero)
        {
            linked.CancelAfter(_drainTimeout);
        }

        foreach (var s in sessions)
        {
            if (linked.IsCancellationRequested)
            {
                break;
            }

            // Best-effort: unregister routes fast so other nodes stop forwarding to a draining pod.
            try
            {
                await _admission.UnregisterAsync(
                    s.TenantId,
                    s.UserId,
                    s.DeviceId,
                    _nodeId,
                    s.ConnectionId,
                    linked.Token);
            }
            catch
            {
            }

            // Best-effort: close the websocket so clients can reconnect elsewhere quickly.
            try
            {
                if (s.Socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                {
                    await s.Socket.CloseAsync(
                        (WebSocketCloseStatus)MicsProtocolCodes.CloseServerDraining,
                        "server draining",
                        linked.Token);
                }
            }
            catch
            {
            }
        }

        _metrics.CounterInc("mics_shutdown_drain_total", 1, ("node", _nodeId));
        _logger.LogWarning("shutdown_drain_done node={NodeId}", _nodeId);
    }
}

