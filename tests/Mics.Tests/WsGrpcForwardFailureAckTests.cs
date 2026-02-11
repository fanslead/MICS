using System.Net.WebSockets;
using System.Reflection;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Mics.Contracts.Hook.V1;
using Mics.Contracts.Message.V1;
using Mics.Contracts.Node.V1;
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

namespace Mics.Tests;

public sealed class WsGrpcForwardFailureAckTests
{
    [Fact]
    public async Task SingleChat_GrpcForwardFailure_WithoutOfflineFallback_ReturnsFailedAck()
    {
        using var ws = new ScriptedWebSocket();

        var msg = new MessageRequest
        {
            MsgId = "m1",
            MsgType = MessageType.SingleChat,
            ToUserId = "u2",
            MsgBody = ByteString.CopyFrom(new byte[] { 1 }),
            TimestampMs = 1,
        };
        ws.EnqueueReceive(WebSocketMessageType.Binary, new ClientFrame { Message = msg }.ToByteArray(), endOfMessage: true);

        var metrics = new MetricsRegistry();
        var handler = new WsGatewayHandler(
            nodeId: "node-1",
            publicEndpoint: "http://localhost:8080",
            tenantAuthMap: new Dictionary<string, string>(StringComparer.Ordinal) { ["t1"] = "http://hook" },
            hook: new AllowAllHookClient(),
            connections: new ConnectionRegistry(),
            routes: new FixedRoutes(new Dictionary<string, OnlineDeviceRoute>(StringComparer.Ordinal)
            {
                ["d2"] = new OnlineDeviceRoute(NodeId: "node-2", Endpoint: "http://n2", ConnectionId: "c2", OnlineAtUnixMs: 1),
            }),
            admission: new NoopAdmission(),
            nodes: new EmptyNodeSnapshot(),
            nodeClients: new FailingNodeClientPool(StatusCode.Unavailable),
            grpcBreaker: new GrpcNodeCircuitBreaker(TimeProvider.System),
            grpcBreakerPolicy: new GrpcBreakerPolicy(5, TimeSpan.FromSeconds(5)),
            offline: new RejectOffline(),
            rateLimiter: new AllowRateLimiter(),
            dedup: new AllowDedup(),
            mq: new MqEventDispatcher(new NoopMqProducer(), metrics, TimeProvider.System, new MqEventDispatcherOptions(QueueCapacity: 1, MaxPendingPerTenant: 1, MaxAttempts: 1, RetryBackoffBase: TimeSpan.Zero, IdleDelay: TimeSpan.Zero)),
            metrics: metrics,
            logger: NullLogger<WsGatewayHandler>.Instance,
            traceContext: new TraceContext(),
            shutdown: new ShutdownState(),
            maxMessageBytes: 0,
            groupRouteChunkSize: 256,
            groupOfflineBufferMaxUsers: 0,
            groupMembersMaxUsers: 1_000);

        var session = new ConnectionSession("t1", "u1", "d1", "c1", "tr1", ws, new TenantRuntimeConfig { TenantMaxMessageQps = 0 });
        await InvokeReceiveLoopAsync(handler, session);

        var ack = ParseServerFrames(ws).Single(f => f.PayloadCase == ServerFrame.PayloadOneofCase.Ack).Ack;
        Assert.Equal("m1", ack.MsgId);
        Assert.Equal(AckStatus.Failed, ack.Status);
    }

    private static async Task InvokeReceiveLoopAsync(WsGatewayHandler handler, ConnectionSession session)
    {
        var mi = typeof(WsGatewayHandler).GetMethod("ReceiveLoopAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(mi);
        var task = (Task)mi!.Invoke(handler, new object[] { session, CancellationToken.None })!;
        await task;
    }

    private static IReadOnlyList<ServerFrame> ParseServerFrames(ScriptedWebSocket ws)
    {
        var frames = new List<ServerFrame>();
        foreach (var send in ws.Sends)
        {
            if (send.Type != WebSocketMessageType.Binary)
            {
                continue;
            }
            frames.Add(ServerFrame.Parser.ParseFrom(send.Payload));
        }
        return frames;
    }

    private sealed class ScriptedWebSocket : WebSocket
    {
        private readonly Queue<(WebSocketMessageType Type, byte[] Payload, bool EndOfMessage)> _receives = new();
        private readonly List<(WebSocketMessageType Type, byte[] Payload, bool EndOfMessage)> _sends = new();
        private WebSocketState _state = WebSocketState.Open;

        public IReadOnlyList<(WebSocketMessageType Type, byte[] Payload, bool EndOfMessage)> Sends => _sends;

        public void EnqueueReceive(WebSocketMessageType type, byte[] payload, bool endOfMessage) =>
            _receives.Enqueue((type, payload, endOfMessage));

        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State => _state;
        public override string? SubProtocol => null;

        public override void Abort() => _state = WebSocketState.Aborted;

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

        public override void Dispose() => _state = WebSocketState.Closed;

        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken) =>
            ReceiveAsync(buffer.AsMemory(), cancellationToken).AsTask().ContinueWith(
                t => new WebSocketReceiveResult(t.Result.Count, t.Result.MessageType, t.Result.EndOfMessage),
                cancellationToken);

        public override ValueTask<ValueWebSocketReceiveResult> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            if (_receives.Count == 0)
            {
                _state = WebSocketState.CloseReceived;
                return new ValueTask<ValueWebSocketReceiveResult>(new ValueWebSocketReceiveResult(0, WebSocketMessageType.Close, true));
            }

            var (type, payload, eom) = _receives.Dequeue();
            payload.CopyTo(buffer);
            return new ValueTask<ValueWebSocketReceiveResult>(new ValueWebSocketReceiveResult(payload.Length, type, eom));
        }

        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken) =>
            SendAsync(buffer.AsMemory(), messageType, endOfMessage ? WebSocketMessageFlags.EndOfMessage : WebSocketMessageFlags.None, cancellationToken).AsTask();

        public override ValueTask SendAsync(ReadOnlyMemory<byte> buffer, WebSocketMessageType messageType, WebSocketMessageFlags messageFlags, CancellationToken cancellationToken)
        {
            _sends.Add((messageType, buffer.ToArray(), (messageFlags & WebSocketMessageFlags.EndOfMessage) != 0));
            return ValueTask.CompletedTask;
        }
    }

    private sealed class AllowAllHookClient : IHookClient
    {
        public ValueTask<AuthResult> AuthAsync(string authHookBaseUrl, string tenantId, string token, string deviceId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<CheckMessageResult> CheckMessageAsync(TenantRuntimeConfig tenantConfig, string tenantId, MessageRequest message, CancellationToken cancellationToken) =>
            new(new CheckMessageResult(true, false, ""));

        public ValueTask<GroupMembersResult> GetGroupMembersAsync(TenantRuntimeConfig tenantConfig, string tenantId, string groupId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<GetOfflineMessagesResult> GetOfflineMessagesAsync(TenantRuntimeConfig tenantConfig, string tenantId, string userId, string deviceId, int maxMessages, string cursor, CancellationToken cancellationToken) =>
            new(new GetOfflineMessagesResult(true, false, "", Array.Empty<MessageRequest>(), "", false));
    }

    private sealed class AllowDedup : IMessageDeduplicator
    {
        public ValueTask<bool> TryMarkAsync(string tenantId, string msgId, TimeSpan ttl, CancellationToken cancellationToken) => new(true);
    }

    private sealed class AllowRateLimiter : IRedisRateLimiter
    {
        public ValueTask<bool> TryConsumeTenantMessageAsync(string tenantId, int maxQps, CancellationToken cancellationToken) => new(true);
    }

    private sealed class FixedRoutes : IOnlineRouteStore
    {
        private readonly IReadOnlyDictionary<string, OnlineDeviceRoute> _routes;

        public FixedRoutes(IReadOnlyDictionary<string, OnlineDeviceRoute> routes)
        {
            _routes = routes;
        }

        public ValueTask UpsertAsync(string tenantId, string userId, string deviceId, OnlineDeviceRoute route, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask RemoveAsync(string tenantId, string userId, string deviceId, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask<IReadOnlyDictionary<string, OnlineDeviceRoute>> GetAllDevicesAsync(string tenantId, string userId, CancellationToken cancellationToken) => new(_routes);
        public ValueTask<IReadOnlyDictionary<string, IReadOnlyList<OnlineDeviceRoute>>> GetAllDevicesForUsersAsync(string tenantId, IReadOnlyList<string> userIds, CancellationToken cancellationToken) => new(new Dictionary<string, IReadOnlyList<OnlineDeviceRoute>>(StringComparer.Ordinal));
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

    private sealed class RejectOffline : IOfflineBufferStore
    {
        public bool TryAdd(string tenantId, string userId, ByteString serverFrameBytes, TimeSpan ttl) => false;
        public IReadOnlyList<ByteString> Drain(string tenantId, string userId) => Array.Empty<ByteString>();
    }

    private sealed class ThrowingCallInvoker : CallInvoker
    {
        private readonly StatusCode _code;

        public ThrowingCallInvoker(StatusCode code)
        {
            _code = code;
        }

        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
        {
            var ex = new RpcException(new Status(_code, "unavailable"));
            return new AsyncUnaryCall<TResponse>(
                Task.FromException<TResponse>(ex),
                Task.FromResult(new Metadata()),
                () => new Status(_code, "unavailable"),
                () => new Metadata(),
                () => { });
        }

        public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options)
            where TRequest : class
            where TResponse : class => throw new NotSupportedException();

        public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options)
            where TRequest : class
            where TResponse : class => throw new NotSupportedException();

        public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
            where TRequest : class
            where TResponse : class => throw new NotSupportedException();

        public override TResponse BlockingUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request) => throw new NotSupportedException();
    }

    private sealed class FailingNodeClientPool : INodeClientPool
    {
        private readonly NodeGateway.NodeGatewayClient _client;

        public FailingNodeClientPool(StatusCode code)
        {
            _client = new NodeGateway.NodeGatewayClient(new ThrowingCallInvoker(code));
        }

        public NodeGateway.NodeGatewayClient Get(string endpoint) => _client;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
