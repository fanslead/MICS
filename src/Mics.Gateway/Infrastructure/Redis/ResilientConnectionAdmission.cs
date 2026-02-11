using Microsoft.Extensions.Logging;
using Mics.Gateway.Metrics;

namespace Mics.Gateway.Infrastructure.Redis;

internal sealed class ResilientConnectionAdmission : IConnectionAdmission
{
    private readonly IConnectionAdmission _inner;
    private readonly AdmissionUnregisterRetryQueue _queue;
    private readonly MetricsRegistry _metrics;
    private readonly ILogger<ResilientConnectionAdmission> _logger;

    public ResilientConnectionAdmission(
        IConnectionAdmission inner,
        AdmissionUnregisterRetryQueue queue,
        MetricsRegistry metrics,
        ILogger<ResilientConnectionAdmission> logger)
    {
        _inner = inner;
        _queue = queue;
        _metrics = metrics;
        _logger = logger;
    }

    public ValueTask<ConnectionAdmissionResult> TryRegisterAsync(
        string tenantId,
        string userId,
        string deviceId,
        OnlineDeviceRoute route,
        int heartbeatTimeoutSeconds,
        int tenantMaxConnections,
        int userMaxConnections,
        CancellationToken cancellationToken) =>
        _inner.TryRegisterAsync(tenantId, userId, deviceId, route, heartbeatTimeoutSeconds, tenantMaxConnections, userMaxConnections, cancellationToken);

    public ValueTask RenewLeaseAsync(
        string tenantId,
        string userId,
        string deviceId,
        int heartbeatTimeoutSeconds,
        CancellationToken cancellationToken) =>
        _inner.RenewLeaseAsync(tenantId, userId, deviceId, heartbeatTimeoutSeconds, cancellationToken);

    public async ValueTask UnregisterAsync(
        string tenantId,
        string userId,
        string deviceId,
        string expectedNodeId,
        string expectedConnectionId,
        CancellationToken cancellationToken)
    {
        try
        {
            await _inner.UnregisterAsync(tenantId, userId, deviceId, expectedNodeId, expectedConnectionId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _metrics.CounterInc("mics_admission_unregister_failed_total", 1, ("tenant", tenantId));
            _logger.LogWarning(ex, "admission_unregister_failed tenant={TenantId} user={UserId} device={DeviceId}", tenantId, userId, deviceId);

            var enqueued = _queue.TryEnqueue(new AdmissionUnregisterWorkItem(
                tenantId,
                userId,
                deviceId,
                expectedNodeId,
                expectedConnectionId,
                Attempt: 1));

            if (!enqueued)
            {
                _metrics.CounterInc("mics_admission_unregister_retry_dropped_total", 1, ("tenant", tenantId));
            }
        }
    }
}
