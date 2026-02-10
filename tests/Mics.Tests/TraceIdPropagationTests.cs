using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
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

namespace Mics.Tests;

public sealed class TraceIdPropagationTests
{
    private sealed class ScriptedWebSocket : WebSocket
    {
        private readonly Queue<(WebSocketMessageType Type, byte[] Payload, bool EndOfMessage)> _receives = new();
        private WebSocketState _state = WebSocketState.Open;

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
            Task.CompletedTask;
    }

    private sealed class TraceAssertingHook : IHookClient
    {
        private readonly ITraceContext _traceContext;
        private readonly string _expected;

        public TraceAssertingHook(ITraceContext traceContext, string expected)
        {
            _traceContext = traceContext;
            _expected = expected;
        }

        public ValueTask<AuthResult> AuthAsync(string authHookBaseUrl, string tenantId, string token, string deviceId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<CheckMessageResult> CheckMessageAsync(TenantRuntimeConfig tenantConfig, string tenantId, MessageRequest message, CancellationToken cancellationToken)
        {
            Assert.Equal(_expected, _traceContext.TraceId);
            return new ValueTask<CheckMessageResult>(new CheckMessageResult(true, false, ""));
        }

        public ValueTask<GroupMembersResult> GetGroupMembersAsync(TenantRuntimeConfig tenantConfig, string tenantId, string groupId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class AllowDedup : IMessageDeduplicator
    {
        public ValueTask<bool> TryMarkAsync(string tenantId, string msgId, TimeSpan ttl, CancellationToken cancellationToken) => new(true);
    }

    private sealed class AllowRateLimiter : IRedisRateLimiter
    {
        public ValueTask<bool> TryConsumeTenantMessageAsync(string tenantId, int maxQps, CancellationToken cancellationToken) => new(true);
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
        public bool TryAdd(string tenantId, string toUserId, byte[] serverFrameBytes, TimeSpan ttl) => true;
        public IReadOnlyList<byte[]> Drain(string tenantId, string userId) => Array.Empty<byte[]>();
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public byte[]? LastBody { get; private set; }
        public required HttpStatusCode Code { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastBody = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
            var resp = new HttpResponseMessage(Code)
            {
                Content = new ByteArrayContent(Array.Empty<byte>()),
            };
            resp.Content.Headers.ContentType = new MediaTypeHeaderValue("application/protobuf");
            return resp;
        }
    }

    [Fact]
    public void HookMetaFactory_IncludesTraceId_FromTraceContext()
    {
        var trace = new TraceContext { TraceId = "tr1" };
        var factory = new DefaultHookMetaFactory(TimeProvider.System, trace);
        var meta = factory.Create("t1");
        Assert.Equal("tr1", meta.TraceId);
    }

    [Fact]
    public async Task WsReceiveLoop_SetsTraceContext_FromSession()
    {
        using var ws = new ScriptedWebSocket();
        var traceContext = new TraceContext();

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
            hook: new TraceAssertingHook(traceContext, "tr1"),
            connections: new ConnectionRegistry(),
            routes: new NoopRoutes(),
            admission: new NoopAdmission(),
            nodes: new EmptyNodeSnapshot(),
            nodeClients: new NoopNodeClientPool(),
            offline: new NoopOffline(),
            rateLimiter: new AllowRateLimiter(),
            dedup: new AllowDedup(),
            mq: new MqEventDispatcher(new NoopMqProducer(), metrics, TimeProvider.System, new MqEventDispatcherOptions(QueueCapacity: 1, MaxAttempts: 1, RetryBackoffBase: TimeSpan.Zero, IdleDelay: TimeSpan.Zero)),
            metrics: metrics,
            logger: NullLogger<WsGatewayHandler>.Instance,
            traceContext: traceContext,
            shutdown: new ShutdownState(),
            maxMessageBytes: 0,
            groupRouteChunkSize: 64,
            groupOfflineBufferMaxUsers: 0,
            groupMembersMaxUsers: 1_000);

        var session = new ConnectionSession("t1", "u1", "d1", "c1", "tr1", ws, new TenantRuntimeConfig { TenantMaxMessageQps = 0 });

        var mi = typeof(WsGatewayHandler).GetMethod("ReceiveLoopAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(mi);
        var task = (Task)mi!.Invoke(handler, new object[] { session, CancellationToken.None })!;
        await task;
    }

    [Fact]
    public async Task HookClient_SendsTraceId_InHookMeta()
    {
        var traceContext = new TraceContext { TraceId = "tr1" };
        var handler = new CaptureHandler { Code = HttpStatusCode.InternalServerError };
        var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };

        var metrics = new MetricsRegistry();
        var limiter = new HookConcurrencyLimiter(metrics);
        var policies = new TenantHookPolicyCache(new HookPolicyDefaults(
            MaxConcurrencyDefault: 8,
            TenantMaxConcurrencyFallback: new Dictionary<string, int>(),
            QueueTimeoutDefault: TimeSpan.FromSeconds(1),
            BreakerFailureThresholdDefault: 100,
            BreakerOpenDurationDefault: TimeSpan.FromSeconds(1),
            SignRequiredDefault: false));

        var client = new HookClient(
            http,
            timeout: TimeSpan.FromMilliseconds(50),
            breaker: new HookCircuitBreaker(TimeProvider.System),
            metaFactory: new DefaultHookMetaFactory(TimeProvider.System, traceContext),
            authSecrets: new AuthHookSecretProvider(new Dictionary<string, string>()),
            policies: policies,
            concurrencyLimiter: limiter,
            metrics: metrics,
            logger: NullLogger<HookClient>.Instance,
            timeProvider: TimeProvider.System);

        var cfg = new TenantRuntimeConfig { HookBaseUrl = "http://hook", TenantSecret = "" };
        var msg = new MessageRequest
        {
            TenantId = "t1",
            UserId = "u1",
            DeviceId = "d1",
            MsgId = "m1",
            MsgType = MessageType.SingleChat,
            ToUserId = "u2",
            MsgBody = ByteString.CopyFrom(new byte[] { 1 }),
            TimestampMs = 1,
        };

        _ = await client.CheckMessageAsync(cfg, "t1", msg, CancellationToken.None);

        Assert.NotNull(handler.LastBody);
        var parsed = CheckMessageRequest.Parser.ParseFrom(handler.LastBody);
        Assert.Equal("tr1", parsed.Meta.TraceId);
    }
}
