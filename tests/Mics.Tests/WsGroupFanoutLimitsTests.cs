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

public sealed class WsGroupFanoutLimitsTests
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

    private sealed class AllowAllHookClient : IHookClient
    {
        private readonly IReadOnlyList<string> _members;

        public AllowAllHookClient(IReadOnlyList<string> members) => _members = members;

        public ValueTask<AuthResult> AuthAsync(string authHookBaseUrl, string tenantId, string token, string deviceId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<CheckMessageResult> CheckMessageAsync(TenantRuntimeConfig tenantConfig, string tenantId, MessageRequest message, CancellationToken cancellationToken) =>
            new(new CheckMessageResult(true, false, ""));

        public ValueTask<GroupMembersResult> GetGroupMembersAsync(TenantRuntimeConfig tenantConfig, string tenantId, string groupId, CancellationToken cancellationToken) =>
            new(new GroupMembersResult(true, false, "", _members));
    }

    private sealed class AllowDedup : IMessageDeduplicator
    {
        public ValueTask<bool> TryMarkAsync(string tenantId, string msgId, TimeSpan ttl, CancellationToken cancellationToken) => new(true);
    }

    private sealed class AllowRateLimiter : IRedisRateLimiter
    {
        public ValueTask<bool> TryConsumeTenantMessageAsync(string tenantId, int maxQps, CancellationToken cancellationToken) =>
            new(true);
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

        public ValueTask RenewLeaseAsync(string tenantId, string userId, string deviceId, int heartbeatTimeoutSeconds, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask UnregisterAsync(string tenantId, string userId, string deviceId, string expectedNodeId, string expectedConnectionId, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }

    private sealed class CountingOfflineStore : IOfflineBufferStore
    {
        public int Adds { get; private set; }

        public bool TryAdd(string tenantId, string toUserId, byte[] serverFrameBytes, TimeSpan ttl)
        {
            Adds++;
            return true;
        }

        public IReadOnlyList<byte[]> Drain(string tenantId, string userId) => Array.Empty<byte[]>();
    }

    private sealed class BlockingBatchRoutes : IOnlineRouteStore
    {
        private int _batchCalls;
        private readonly TaskCompletionSource _firstStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _secondStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int BatchCalls => Volatile.Read(ref _batchCalls);
        public Task FirstStarted => _firstStarted.Task;
        public Task SecondStarted => _secondStarted.Task;
        public void Release() => _release.TrySetResult();

        public ValueTask UpsertAsync(string tenantId, string userId, string deviceId, OnlineDeviceRoute route, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask RemoveAsync(string tenantId, string userId, string deviceId, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask<IReadOnlyDictionary<string, OnlineDeviceRoute>> GetAllDevicesAsync(string tenantId, string userId, CancellationToken cancellationToken) =>
            new(new Dictionary<string, OnlineDeviceRoute>(StringComparer.Ordinal));

        public async ValueTask<IReadOnlyDictionary<string, IReadOnlyList<OnlineDeviceRoute>>> GetAllDevicesForUsersAsync(string tenantId, IReadOnlyList<string> userIds, CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref _batchCalls);
            if (call == 1) _firstStarted.TrySetResult();
            if (call == 2) _secondStarted.TrySetResult();

            await _release.Task.WaitAsync(cancellationToken);
            return new Dictionary<string, IReadOnlyList<OnlineDeviceRoute>>(StringComparer.Ordinal);
        }

        public ValueTask<long> GetDeviceCountAsync(string tenantId, string userId, CancellationToken cancellationToken) => new(0);
    }

    [Fact]
    public async Task GroupChat_OfflineBuffer_IsCapped()
    {
        using var ws = new ScriptedWebSocket();

        var members = Enumerable.Range(1, 50).Select(i => "u" + i).ToArray();
        var hook = new AllowAllHookClient(members);

        var metrics = new MetricsRegistry();
        var offline = new CountingOfflineStore();
        var mq = new MqEventDispatcher(new NoopMqProducer(), metrics, TimeProvider.System, new MqEventDispatcherOptions(QueueCapacity: 1, MaxAttempts: 1, RetryBackoffBase: TimeSpan.Zero, IdleDelay: TimeSpan.Zero));

        var handler = new WsGatewayHandler(
            nodeId: "node-1",
            publicEndpoint: "http://localhost:8080",
            tenantAuthMap: new Dictionary<string, string>(StringComparer.Ordinal) { ["t1"] = "http://hook" },
            hook: hook,
            connections: new ConnectionRegistry(),
            routes: new NoopRoutes(),
            admission: new NoopAdmission(),
            nodes: new EmptyNodeSnapshot(),
            nodeClients: new NoopNodeClientPool(),
            offline: offline,
            rateLimiter: new AllowRateLimiter(),
            dedup: new AllowDedup(),
            mq: mq,
            metrics: metrics,
            logger: NullLogger<WsGatewayHandler>.Instance,
            traceContext: new TraceContext(),
            shutdown: new ShutdownState(),
            maxMessageBytes: 0,
            groupRouteChunkSize: 10,
            groupOfflineBufferMaxUsers: 7,
            groupMembersMaxUsers: 1_000);

        var msg = new MessageRequest
        {
            MsgId = "m1",
            MsgType = MessageType.GroupChat,
            GroupId = "group-1",
            MsgBody = ByteString.CopyFrom(new byte[] { 1 }),
        };
        ws.EnqueueReceive(WebSocketMessageType.Binary, new ClientFrame { Message = msg }.ToByteArray(), endOfMessage: true);

        var session = new ConnectionSession("t1", "u0", "d0", "c1", "tr1", ws, new TenantRuntimeConfig { OfflineBufferTtlSeconds = 60 });

        await InvokeReceiveLoopAsync(handler, session);

        Assert.Equal(7, offline.Adds);
    }

    [Fact]
    public async Task GroupChat_RouteFetch_PrefetchesNextChunk()
    {
        using var ws = new ScriptedWebSocket();

        var members = new[] { "u1", "u2" };
        var hook = new AllowAllHookClient(members);

        var metrics = new MetricsRegistry();
        var offline = new CountingOfflineStore();
        var routes = new BlockingBatchRoutes();
        var mq = new MqEventDispatcher(new NoopMqProducer(), metrics, TimeProvider.System, new MqEventDispatcherOptions(QueueCapacity: 1, MaxAttempts: 1, RetryBackoffBase: TimeSpan.Zero, IdleDelay: TimeSpan.Zero));

        var handler = new WsGatewayHandler(
            nodeId: "node-1",
            publicEndpoint: "http://localhost:8080",
            tenantAuthMap: new Dictionary<string, string>(StringComparer.Ordinal) { ["t1"] = "http://hook" },
            hook: hook,
            connections: new ConnectionRegistry(),
            routes: routes,
            admission: new NoopAdmission(),
            nodes: new EmptyNodeSnapshot(),
            nodeClients: new NoopNodeClientPool(),
            offline: offline,
            rateLimiter: new AllowRateLimiter(),
            dedup: new AllowDedup(),
            mq: mq,
            metrics: metrics,
            logger: NullLogger<WsGatewayHandler>.Instance,
            traceContext: new TraceContext(),
            shutdown: new ShutdownState(),
            maxMessageBytes: 0,
            groupRouteChunkSize: 1,
            groupOfflineBufferMaxUsers: 10,
            groupMembersMaxUsers: 1_000);

        var msg = new MessageRequest
        {
            MsgId = "m2",
            MsgType = MessageType.GroupChat,
            GroupId = "group-1",
            MsgBody = ByteString.CopyFrom(new byte[] { 1 }),
        };
        ws.EnqueueReceive(WebSocketMessageType.Binary, new ClientFrame { Message = msg }.ToByteArray(), endOfMessage: true);

        var session = new ConnectionSession("t1", "u0", "d0", "c1", "tr1", ws, new TenantRuntimeConfig { OfflineBufferTtlSeconds = 60 });

        var loop = InvokeReceiveLoopAsync(handler, session);

        await routes.FirstStarted.WaitAsync(TimeSpan.FromSeconds(1));
        await routes.SecondStarted.WaitAsync(TimeSpan.FromMilliseconds(200));

        routes.Release();
        await loop;

        Assert.Equal(2, routes.BatchCalls);
    }

    private static async Task InvokeReceiveLoopAsync(WsGatewayHandler handler, ConnectionSession session)
    {
        var mi = typeof(WsGatewayHandler).GetMethod("ReceiveLoopAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(mi);
        var task = (Task)mi!.Invoke(handler, new object[] { session, CancellationToken.None })!;
        await task;
    }
}
