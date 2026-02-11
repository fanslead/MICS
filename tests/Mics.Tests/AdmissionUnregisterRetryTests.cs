using Microsoft.Extensions.Logging.Abstractions;
using Mics.Gateway.Infrastructure.Redis;
using Mics.Gateway.Metrics;

namespace Mics.Tests;

public sealed class AdmissionUnregisterRetryTests
{
    private sealed class ThrowingAdmission : IConnectionAdmission
    {
        public ValueTask<ConnectionAdmissionResult> TryRegisterAsync(
            string tenantId,
            string userId,
            string deviceId,
            OnlineDeviceRoute route,
            int heartbeatTimeoutSeconds,
            int tenantMaxConnections,
            int userMaxConnections,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask RenewLeaseAsync(string tenantId, string userId, string deviceId, int heartbeatTimeoutSeconds, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask UnregisterAsync(string tenantId, string userId, string deviceId, string expectedNodeId, string expectedConnectionId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("redis down");
    }

    [Fact]
    public async Task Unregister_Failure_IsEnqueuedAndDoesNotThrow()
    {
        var metrics = new MetricsRegistry();
        var queue = new AdmissionUnregisterRetryQueue(capacity: 8);
        var admission = new ResilientConnectionAdmission(
            inner: new ThrowingAdmission(),
            queue: queue,
            metrics: metrics,
            logger: NullLogger<ResilientConnectionAdmission>.Instance);

        await admission.UnregisterAsync("t1", "u1", "d1", "node-1", "c1", CancellationToken.None);

        Assert.Equal(1, queue.Pending);
        Assert.True(queue.Reader.TryRead(out var item));
        queue.OnDequeued();

        Assert.Equal("t1", item.TenantId);
        Assert.Equal("u1", item.UserId);
        Assert.Equal("d1", item.DeviceId);
        Assert.Equal("node-1", item.ExpectedNodeId);
        Assert.Equal("c1", item.ExpectedConnectionId);
        Assert.Equal(1, item.Attempt);

        var prom = metrics.CollectPrometheusText();
        Assert.Contains("mics_admission_unregister_failed_total{tenant=\"t1\"} 1", prom);
    }
}
