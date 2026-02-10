using System.Net.WebSockets;
using System.Reflection;
using Google.Protobuf;
using Microsoft.Extensions.Logging.Abstractions;
using Mics.Contracts.Hook.V1;
using Mics.Contracts.Message.V1;
using Mics.Gateway.Connections;
using Mics.Gateway.Metrics;
using Mics.Gateway.Protocol;
using Mics.Gateway.Infrastructure.Redis;

namespace Mics.Tests;

public sealed class HeartbeatProtoTests
{
    [Fact]
    public void ClientFrame_HeartbeatPing_RoundTrip()
    {
        var ping = new ClientFrame
        {
            HeartbeatPing = new HeartbeatPing { TimestampMs = 123 },
        };

        var parsed = ClientFrame.Parser.ParseFrom(ping.ToByteArray());
        Assert.Equal(ClientFrame.PayloadOneofCase.HeartbeatPing, parsed.PayloadCase);
        Assert.Equal(123, parsed.HeartbeatPing.TimestampMs);
    }
}

public sealed class HeartbeatSweeperTests
{
    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _now;

        public ManualTimeProvider(DateTimeOffset now)
        {
            _now = now;
        }

        public void Set(DateTimeOffset now) => _now = now;

        public override DateTimeOffset GetUtcNow() => _now;
    }

    private sealed class FakeRegistry : IConnectionRegistry
    {
        private readonly IReadOnlyList<ConnectionSession> _sessions;

        public FakeRegistry(IReadOnlyList<ConnectionSession> sessions)
        {
            _sessions = sessions;
        }

        public bool TryAdd(ConnectionSession session) => throw new NotSupportedException();
        public bool TryRemove(string tenantId, string userId, string deviceId, out ConnectionSession? removed) => throw new NotSupportedException();
        public bool TryGet(string tenantId, string userId, string deviceId, out ConnectionSession? session) => throw new NotSupportedException();
        public IReadOnlyList<ConnectionSession> GetAllForUser(string tenantId, string userId) => throw new NotSupportedException();
        public IReadOnlyList<ConnectionSession> GetAllSessionsSnapshot() => _sessions;
    }

    private sealed class FakeWebSocket : WebSocket
    {
        private WebSocketState _state = WebSocketState.Open;

        public WebSocketCloseStatus? ClosedStatus { get; private set; }
        public string? ClosedDescription { get; private set; }

        public override WebSocketCloseStatus? CloseStatus => ClosedStatus;
        public override string? CloseStatusDescription => ClosedDescription;
        public override WebSocketState State => _state;
        public override string? SubProtocol => null;

        public override void Abort() => _state = WebSocketState.Aborted;
        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
        {
            ClosedStatus = closeStatus;
            ClosedDescription = statusDescription;
            _state = WebSocketState.Closed;
            return Task.CompletedTask;
        }

        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) =>
            CloseAsync(closeStatus, statusDescription, cancellationToken);

        public override void Dispose() => _state = WebSocketState.Closed;

        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class FakeAdmission : IConnectionAdmission
    {
        public int RenewCalls { get; private set; }

        public ValueTask<ConnectionAdmissionResult> TryRegisterAsync(
            string tenantId,
            string userId,
            string deviceId,
            OnlineDeviceRoute route,
            int heartbeatTimeoutSeconds,
            int tenantMaxConnections,
            int userMaxConnections,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask RenewLeaseAsync(string tenantId, string userId, string deviceId, int heartbeatTimeoutSeconds, CancellationToken cancellationToken)
        {
            RenewCalls++;
            return ValueTask.CompletedTask;
        }

        public ValueTask UnregisterAsync(string tenantId, string userId, string deviceId, string expectedNodeId, string expectedConnectionId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    [Fact]
    public async Task Sweeper_Closes_ExpiredConnections()
    {
        var tp = new ManualTimeProvider(DateTimeOffset.FromUnixTimeMilliseconds(10_000));
        var ws = new FakeWebSocket();
        var cfg = new TenantRuntimeConfig { HeartbeatTimeoutSeconds = 1 };
        var session = new ConnectionSession("t1", "u1", "d1", "c1", "tr1", ws, cfg);
        session.Touch(8_000); // now=10_000 => idle 2s > 1s timeout

        var reg = new FakeRegistry(new[] { session });
        var metrics = new MetricsRegistry();
        var sweeper = new HeartbeatSweeper(reg, new FakeAdmission(), metrics, NullLogger<HeartbeatSweeper>.Instance, tp);

        await sweeper.SweepOnceAsync(CancellationToken.None);

        Assert.Equal((WebSocketCloseStatus)MicsProtocolCodes.CloseHeartbeatTimeout, ws.ClosedStatus);
        Assert.Equal("heartbeat timeout", ws.ClosedDescription);
    }

    [Fact]
    public async Task Sweeper_RenewsLease_ForActiveConnections()
    {
        var tp = new ManualTimeProvider(DateTimeOffset.FromUnixTimeMilliseconds(10_000));
        var ws = new FakeWebSocket();
        var cfg = new TenantRuntimeConfig { HeartbeatTimeoutSeconds = 30 };
        var session = new ConnectionSession("t1", "u1", "d1", "c1", "tr1", ws, cfg);
        session.Touch(10_000);

        var leaseField = typeof(ConnectionSession).GetField("_lastLeaseRenewUnixMs", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(leaseField);
        leaseField!.SetValue(session, -10_000L);

        var reg = new FakeRegistry(new[] { session });
        var metrics = new MetricsRegistry();
        var admission = new FakeAdmission();
        var sweeper = new HeartbeatSweeper(reg, admission, metrics, NullLogger<HeartbeatSweeper>.Instance, tp);

        await sweeper.SweepOnceAsync(CancellationToken.None);

        Assert.Equal(1, admission.RenewCalls);
        Assert.Null(ws.ClosedStatus);
    }

    [Fact]
    public async Task Sweeper_DoesNotRenewLease_TooFrequently()
    {
        var tp = new ManualTimeProvider(DateTimeOffset.FromUnixTimeMilliseconds(10_000));
        var ws = new FakeWebSocket();
        var cfg = new TenantRuntimeConfig { HeartbeatTimeoutSeconds = 30 };
        var session = new ConnectionSession("t1", "u1", "d1", "c1", "tr1", ws, cfg);
        session.Touch(10_000);

        var leaseField = typeof(ConnectionSession).GetField("_lastLeaseRenewUnixMs", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(leaseField);
        leaseField!.SetValue(session, 10_000L);

        var reg = new FakeRegistry(new[] { session });
        var metrics = new MetricsRegistry();
        var admission = new FakeAdmission();
        var sweeper = new HeartbeatSweeper(reg, admission, metrics, NullLogger<HeartbeatSweeper>.Instance, tp);

        await sweeper.SweepOnceAsync(CancellationToken.None);

        Assert.Equal(0, admission.RenewCalls);
    }
}
