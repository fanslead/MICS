using System.Net.WebSockets;
using System.Reflection;
using Google.Protobuf;
using Microsoft.Extensions.Logging.Abstractions;
using Mics.Contracts.Hook.V1;
using Mics.Contracts.Message.V1;
using Mics.Gateway.Cluster;
using Mics.Gateway.Connections;
using Mics.Gateway.Grpc;
using Mics.Gateway.Hook;
using Mics.Gateway.Infrastructure;
using Mics.Gateway.Infrastructure.Redis;
using Mics.Gateway.Metrics;
using Mics.Gateway.Mq;
using Mics.Gateway.Offline;
using Mics.Gateway.Ws;
using Xunit;

namespace Mics.Tests;

public sealed class WsMessageSizeLimitTests
{
    [Fact]
    public async Task Frame_TooLarge_IsRejectedBeforeParsing()
    {
        using var ws = new ScriptedWebSocket();

        var msg = new MessageRequest
        {
            MsgId = "m1",
            MsgType = MessageType.SingleChat,
            ToUserId = "u2",
            MsgBody = ByteString.CopyFrom(new byte[70_000]),
            TimestampMs = 1,
        };
        var payload = new ClientFrame { Message = msg }.ToByteArray();

        const int chunk = 8 * 1024;
        for (var i = 0; i < payload.Length; i += chunk)
        {
            var len = Math.Min(chunk, payload.Length - i);
            ws.EnqueueReceive(WebSocketMessageType.Binary, payload.AsSpan(i, len).ToArray(), endOfMessage: i + len >= payload.Length);
        }

        var metrics = new MetricsRegistry();
        var handler = new WsGatewayHandler(
            nodeId: "node-1",
            publicEndpoint: "http://localhost:8080",
            tenantAuthMap: new Dictionary<string, string>(StringComparer.Ordinal) { ["t1"] = "http://hook" },
            hook: new NoopHookClient(),
            connections: new ConnectionRegistry(),
            routes: new NoopRoutes(),
            admission: new NoopAdmission(),
            nodes: new EmptyNodeSnapshot(),
            nodeClients: new NoopNodeClientPool(),
            offline: new NoopOffline(),
            rateLimiter: new AllowRateLimiter(),
            dedup: new AllowDedup(),
            mq: new MqEventDispatcher(new NoopMqProducer(), metrics, TimeProvider.System, new MqEventDispatcherOptions(QueueCapacity: 1, MaxPendingPerTenant: 1, MaxAttempts: 1, RetryBackoffBase: TimeSpan.Zero, IdleDelay: TimeSpan.Zero)),
            metrics: metrics,
            logger: NullLogger<WsGatewayHandler>.Instance,
            traceContext: new TraceContext(),
            shutdown: new ShutdownState(),
            maxMessageBytes: 1,
            groupRouteChunkSize: 256,
            groupOfflineBufferMaxUsers: 0,
            groupMembersMaxUsers: 1_000);

        var session = new ConnectionSession("t1", "u1", "d1", "c1", "tr1", ws, new TenantRuntimeConfig { TenantMaxMessageQps = 0 });
        await InvokeReceiveLoopAsync(handler, session);

        var errors = ws.Sends
            .Where(s => s.Type == WebSocketMessageType.Binary)
            .Select(s => ServerFrame.Parser.ParseFrom(s.Payload))
            .Where(f => f.PayloadCase == ServerFrame.PayloadOneofCase.Error)
            .Select(f => f.Error)
            .ToArray();

        Assert.Single(errors);
        Assert.Equal(4401, errors[0].Code);
    }

    [Fact]
    public async Task MessageBody_TooLarge_IsRejected()
    {
        using var ws = new ScriptedWebSocket();

        var msg = new MessageRequest
        {
            MsgId = "m1",
            MsgType = MessageType.SingleChat,
            ToUserId = "u2",
            MsgBody = ByteString.CopyFrom(new byte[] { 1, 2 }),
            TimestampMs = 1,
        };
        ws.EnqueueReceive(WebSocketMessageType.Binary, new ClientFrame { Message = msg }.ToByteArray(), endOfMessage: true);

        var metrics = new MetricsRegistry();
        var handler = new WsGatewayHandler(
            nodeId: "node-1",
            publicEndpoint: "http://localhost:8080",
            tenantAuthMap: new Dictionary<string, string>(StringComparer.Ordinal) { ["t1"] = "http://hook" },
            hook: new NoopHookClient(),
            connections: new ConnectionRegistry(),
            routes: new NoopRoutes(),
            admission: new NoopAdmission(),
            nodes: new EmptyNodeSnapshot(),
            nodeClients: new NoopNodeClientPool(),
            offline: new NoopOffline(),
            rateLimiter: new AllowRateLimiter(),
            dedup: new AllowDedup(),
            mq: new MqEventDispatcher(new NoopMqProducer(), metrics, TimeProvider.System, new MqEventDispatcherOptions(QueueCapacity: 1, MaxPendingPerTenant: 1, MaxAttempts: 1, RetryBackoffBase: TimeSpan.Zero, IdleDelay: TimeSpan.Zero)),
            metrics: metrics,
            logger: NullLogger<WsGatewayHandler>.Instance,
            traceContext: new TraceContext(),
            shutdown: new ShutdownState(),
            maxMessageBytes: 1,
            groupRouteChunkSize: 256,
            groupOfflineBufferMaxUsers: 0,
            groupMembersMaxUsers: 1_000);

        var session = new ConnectionSession("t1", "u1", "d1", "c1", "tr1", ws, new TenantRuntimeConfig { TenantMaxMessageQps = 0 });
        await InvokeReceiveLoopAsync(handler, session);

        var acks = ws.Sends
            .Where(s => s.Type == WebSocketMessageType.Binary)
            .Select(s => ServerFrame.Parser.ParseFrom(s.Payload))
            .Where(f => f.PayloadCase == ServerFrame.PayloadOneofCase.Ack)
            .Select(f => f.Ack)
            .ToArray();

        Assert.Single(acks);
        Assert.Equal(AckStatus.Failed, acks[0].Status);
        Assert.Equal("msg_body_too_large", acks[0].Reason);
    }

    private static async Task InvokeReceiveLoopAsync(WsGatewayHandler handler, ConnectionSession session)
    {
        var mi = typeof(WsGatewayHandler).GetMethod("ReceiveLoopAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(mi);
        var task = (Task)mi!.Invoke(handler, new object[] { session, CancellationToken.None })!;
        await task;
    }

    private sealed class ScriptedWebSocket : WebSocket
    {
        public sealed record SendItem(WebSocketMessageType Type, byte[] Payload, bool EndOfMessage);

        private readonly Queue<(WebSocketMessageType Type, byte[] Payload, bool EndOfMessage)> _receives = new();
        private WebSocketState _state = WebSocketState.Open;

        public List<SendItem> Sends { get; } = new();

        public void EnqueueReceive(WebSocketMessageType type, byte[] payload, bool endOfMessage) =>
            _receives.Enqueue((type, payload, endOfMessage));

        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State => _state;
        public override string? SubProtocol => null;

        public override void Abort() => _state = WebSocketState.Aborted;
        public override void Dispose() => _state = WebSocketState.Closed;

        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
        {
            _state = WebSocketState.Closed;
            return Task.CompletedTask;
        }

        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
        {
            _state = WebSocketState.CloseSent;
            return Task.CompletedTask;
        }

        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public override ValueTask<ValueWebSocketReceiveResult> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            if (_receives.Count == 0)
            {
                _state = WebSocketState.CloseReceived;
                return new ValueTask<ValueWebSocketReceiveResult>(new ValueWebSocketReceiveResult(0, WebSocketMessageType.Close, true));
            }

            var (type, payload, endOfMessage) = _receives.Dequeue();
            payload.CopyTo(buffer);
            return new ValueTask<ValueWebSocketReceiveResult>(new ValueWebSocketReceiveResult(payload.Length, type, endOfMessage));
        }

        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public override ValueTask SendAsync(ReadOnlyMemory<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
        {
            Sends.Add(new SendItem(messageType, buffer.ToArray(), endOfMessage));
            return ValueTask.CompletedTask;
        }
    }

    private sealed class NoopHookClient : IHookClient
    {
        public ValueTask<AuthResult> AuthAsync(string authHookBaseUrl, string tenantId, string token, string deviceId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<CheckMessageResult> CheckMessageAsync(TenantRuntimeConfig tenantConfig, string tenantId, MessageRequest message, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<GroupMembersResult> GetGroupMembersAsync(TenantRuntimeConfig tenantConfig, string tenantId, string groupId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<GetOfflineMessagesResult> GetOfflineMessagesAsync(TenantRuntimeConfig tenantConfig, string tenantId, string userId, string deviceId, int maxMessages, string cursor, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class NoopRoutes : IOnlineRouteStore
    {
        public ValueTask UpsertAsync(string tenantId, string userId, string deviceId, OnlineDeviceRoute route, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask RemoveAsync(string tenantId, string userId, string deviceId, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask<IReadOnlyDictionary<string, OnlineDeviceRoute>> GetAllDevicesAsync(string tenantId, string userId, CancellationToken cancellationToken) =>
            new(new Dictionary<string, OnlineDeviceRoute>(StringComparer.Ordinal));

        public ValueTask<IReadOnlyDictionary<string, IReadOnlyList<OnlineDeviceRoute>>> GetAllDevicesForUsersAsync(string tenantId, IReadOnlyList<string> userIds, CancellationToken cancellationToken) =>
            new(new Dictionary<string, IReadOnlyList<OnlineDeviceRoute>>(StringComparer.Ordinal));

        public ValueTask<long> GetDeviceCountAsync(string tenantId, string userId, CancellationToken cancellationToken) => new(0);
    }

    private sealed class NoopAdmission : IConnectionAdmission
    {
        public ValueTask<ConnectionAdmissionResult> TryRegisterAsync(string tenantId, string userId, string deviceId, OnlineDeviceRoute route, int heartbeatTimeoutSeconds, int tenantMaxConnections, int userMaxConnections, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask RenewLeaseAsync(string tenantId, string userId, string deviceId, int heartbeatTimeoutSeconds, CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask UnregisterAsync(string tenantId, string userId, string deviceId, string expectedNodeId, string expectedConnectionId, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    private sealed class EmptyNodeSnapshot : INodeSnapshot
    {
        public IReadOnlyList<NodeInfo> Current => Array.Empty<NodeInfo>();
    }

    private sealed class NoopNodeClientPool : INodeClientPool
    {
        public Mics.Contracts.Node.V1.NodeGateway.NodeGatewayClient Get(string endpoint) => throw new NotSupportedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NoopOffline : IOfflineBufferStore
    {
        public bool TryAdd(string tenantId, string userId, byte[] serverFrameBytes, TimeSpan ttl) => true;
        public IReadOnlyList<byte[]> Drain(string tenantId, string userId) => Array.Empty<byte[]>();
    }

    private sealed class AllowRateLimiter : IRedisRateLimiter
    {
        public ValueTask<bool> TryConsumeTenantMessageAsync(string tenantId, int qpsLimit, CancellationToken cancellationToken) => new(true);
    }

    private sealed class AllowDedup : IMessageDeduplicator
    {
        public ValueTask<bool> TryMarkAsync(string tenantId, string msgId, TimeSpan ttl, CancellationToken cancellationToken) => new(true);
    }
}
