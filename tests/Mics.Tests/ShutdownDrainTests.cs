using System.Net.WebSockets;
using Microsoft.Extensions.Logging.Abstractions;
using Mics.Contracts.Hook.V1;
using Mics.Gateway.Connections;
using Mics.Gateway.Infrastructure;
using Mics.Gateway.Infrastructure.Redis;
using Mics.Gateway.Metrics;
using Mics.Gateway.Protocol;

namespace Mics.Tests;

public sealed class ShutdownDrainTests
{
    private sealed class RecordingAdmission : IConnectionAdmission
    {
        public int UnregisterCalls;

        public ValueTask<ConnectionAdmissionResult> TryRegisterAsync(
            string tenantId,
            string userId,
            string deviceId,
            OnlineDeviceRoute route,
            int heartbeatTimeoutSeconds,
            int tenantMaxConnections,
            int userMaxConnections,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new ConnectionAdmissionResult(ConnectionAdmissionStatus.AllowedNew, ""));

        public ValueTask RenewLeaseAsync(string tenantId, string userId, string deviceId, int heartbeatTimeoutSeconds, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask UnregisterAsync(string tenantId, string userId, string deviceId, string expectedNodeId, string expectedConnectionId, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref UnregisterCalls);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class CloseRecordingWebSocket : WebSocket
    {
        private WebSocketState _state = WebSocketState.Open;

        public int CloseCalls;
        public WebSocketCloseStatus? LastCloseStatus;
        public string? LastCloseDescription;

        public override WebSocketCloseStatus? CloseStatus => LastCloseStatus;
        public override string? CloseStatusDescription => LastCloseDescription;
        public override WebSocketState State => _state;
        public override string? SubProtocol => null;

        public override void Abort() => _state = WebSocketState.Aborted;

        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
        {
            CloseCalls++;
            LastCloseStatus = closeStatus;
            LastCloseDescription = statusDescription;
            _state = WebSocketState.Closed;
            return Task.CompletedTask;
        }

        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) =>
            CloseAsync(closeStatus, statusDescription, cancellationToken);

        public override void Dispose() => _state = WebSocketState.Closed;

        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    [Fact]
    public async Task ShutdownDrainService_StopsNewTraffic_AndClosesExistingConnections()
    {
        var state = new ShutdownState();
        var registry = new ConnectionRegistry();
        var admission = new RecordingAdmission();
        var metrics = new MetricsRegistry();

        var ws1 = new CloseRecordingWebSocket();
        var ws2 = new CloseRecordingWebSocket();

        registry.TryAdd(new ConnectionSession("t1", "u1", "d1", "c1", "tr1", ws1, new TenantRuntimeConfig()));
        registry.TryAdd(new ConnectionSession("t1", "u2", "d2", "c2", "tr2", ws2, new TenantRuntimeConfig()));

        var svc = new ShutdownDrainService(
            nodeId: "node-1",
            drainTimeout: TimeSpan.FromSeconds(1),
            shutdown: state,
            connections: registry,
            admission: admission,
            metrics: metrics,
            logger: NullLogger<ShutdownDrainService>.Instance);

        await svc.StopAsync(CancellationToken.None);

        Assert.True(state.IsDraining);
        Assert.Equal(2, admission.UnregisterCalls);
        Assert.Equal(1, ws1.CloseCalls);
        Assert.Equal(1, ws2.CloseCalls);
        Assert.Equal((WebSocketCloseStatus)MicsProtocolCodes.CloseServerDraining, ws1.LastCloseStatus);
        Assert.Equal((WebSocketCloseStatus)MicsProtocolCodes.CloseServerDraining, ws2.LastCloseStatus);
    }
}

