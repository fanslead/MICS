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

public sealed class WsAckSemanticsTests
{
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

    private sealed class ThrowingHookClient : IHookClient
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

    private sealed class ThrowingDedup : IMessageDeduplicator
    {
        public ValueTask<bool> TryMarkAsync(string tenantId, string msgId, TimeSpan ttl, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
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

    private sealed class NoopOffline : IOfflineBufferStore
    {
        public bool TryAdd(string tenantId, string toUserId, ByteString serverFrameBytes, TimeSpan ttl) => true;
        public IReadOnlyList<ByteString> Drain(string tenantId, string userId) => Array.Empty<ByteString>();
    }

    private sealed class DenyCheckHookClient : IHookClient
    {
        public ValueTask<AuthResult> AuthAsync(string authHookBaseUrl, string tenantId, string token, string deviceId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<CheckMessageResult> CheckMessageAsync(TenantRuntimeConfig tenantConfig, string tenantId, MessageRequest message, CancellationToken cancellationToken) =>
            new(new CheckMessageResult(Allow: false, Degraded: false, Reason: "blocked"));

        public ValueTask<GroupMembersResult> GetGroupMembersAsync(TenantRuntimeConfig tenantConfig, string tenantId, string groupId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<GetOfflineMessagesResult> GetOfflineMessagesAsync(TenantRuntimeConfig tenantConfig, string tenantId, string userId, string deviceId, int maxMessages, string cursor, CancellationToken cancellationToken) =>
            new(new GetOfflineMessagesResult(true, false, "", Array.Empty<MessageRequest>(), "", false));
    }

    private sealed class AllowGroupHookClient : IHookClient
    {
        private readonly IReadOnlyList<string> _members;

        public AllowGroupHookClient(IReadOnlyList<string> members) => _members = members;

        public ValueTask<AuthResult> AuthAsync(string authHookBaseUrl, string tenantId, string token, string deviceId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<CheckMessageResult> CheckMessageAsync(TenantRuntimeConfig tenantConfig, string tenantId, MessageRequest message, CancellationToken cancellationToken) =>
            new(new CheckMessageResult(Allow: true, Degraded: false, Reason: ""));

        public ValueTask<GroupMembersResult> GetGroupMembersAsync(TenantRuntimeConfig tenantConfig, string tenantId, string groupId, CancellationToken cancellationToken) =>
            new(new GroupMembersResult(Ok: true, Degraded: false, Reason: "", UserIds: _members));

        public ValueTask<GetOfflineMessagesResult> GetOfflineMessagesAsync(TenantRuntimeConfig tenantConfig, string tenantId, string userId, string deviceId, int maxMessages, string cursor, CancellationToken cancellationToken) =>
            new(new GetOfflineMessagesResult(true, false, "", Array.Empty<MessageRequest>(), "", false));
    }

    private sealed class AllowDedup : IMessageDeduplicator
    {
        public ValueTask<bool> TryMarkAsync(string tenantId, string msgId, TimeSpan ttl, CancellationToken cancellationToken) => new(true);
    }

    private sealed class CountingRoutes : IOnlineRouteStore
    {
        private int _getAllDevicesCalls;
        private readonly IReadOnlyDictionary<string, OnlineDeviceRoute> _routes;

        public CountingRoutes(IReadOnlyDictionary<string, OnlineDeviceRoute> routes)
        {
            _routes = routes;
        }

        public int GetAllDevicesCalls => Volatile.Read(ref _getAllDevicesCalls);

        public ValueTask UpsertAsync(string tenantId, string userId, string deviceId, OnlineDeviceRoute route, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask RemoveAsync(string tenantId, string userId, string deviceId, CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask<IReadOnlyDictionary<string, OnlineDeviceRoute>> GetAllDevicesAsync(string tenantId, string userId, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _getAllDevicesCalls);
            return new ValueTask<IReadOnlyDictionary<string, OnlineDeviceRoute>>(_routes);
        }

        public ValueTask<IReadOnlyDictionary<string, IReadOnlyList<OnlineDeviceRoute>>> GetAllDevicesForUsersAsync(string tenantId, IReadOnlyList<string> userIds, CancellationToken cancellationToken) =>
            new(new Dictionary<string, IReadOnlyList<OnlineDeviceRoute>>(StringComparer.Ordinal));

        public ValueTask<long> GetDeviceCountAsync(string tenantId, string userId, CancellationToken cancellationToken) => new(0);
    }

    private sealed class BlockingGroupHooks : IHookClient
    {
        private readonly bool _allow;
        private readonly IReadOnlyList<string> _members;
        private readonly TaskCompletionSource _checkRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _membersRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _membersStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public BlockingGroupHooks(bool allow, IReadOnlyList<string> members)
        {
            _allow = allow;
            _members = members;
        }

        public Task MembersStarted => _membersStarted.Task;
        public void ReleaseCheck() => _checkRelease.TrySetResult();
        public void ReleaseMembers() => _membersRelease.TrySetResult();

        public ValueTask<AuthResult> AuthAsync(string authHookBaseUrl, string tenantId, string token, string deviceId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public async ValueTask<CheckMessageResult> CheckMessageAsync(TenantRuntimeConfig tenantConfig, string tenantId, MessageRequest message, CancellationToken cancellationToken)
        {
            await _checkRelease.Task;
            return new CheckMessageResult(Allow: _allow, Degraded: false, Reason: _allow ? "" : "blocked");
        }

        public async ValueTask<GroupMembersResult> GetGroupMembersAsync(TenantRuntimeConfig tenantConfig, string tenantId, string groupId, CancellationToken cancellationToken)
        {
            _membersStarted.TrySetResult();
            await _membersRelease.Task;
            return new GroupMembersResult(Ok: true, Degraded: false, Reason: "", UserIds: _members);
        }

        public ValueTask<GetOfflineMessagesResult> GetOfflineMessagesAsync(TenantRuntimeConfig tenantConfig, string tenantId, string userId, string deviceId, int maxMessages, string cursor, CancellationToken cancellationToken) =>
            new(new GetOfflineMessagesResult(true, false, "", Array.Empty<MessageRequest>(), "", false));
    }

    [Fact]
    public async Task Invalid_SingleChat_MissingToUserId_AcksFailed()
    {
        using var ws = new ScriptedWebSocket();

        var frame = new ClientFrame
        {
            Message = new MessageRequest
            {
                MsgId = "m1",
                MsgType = MessageType.SingleChat,
                MsgBody = ByteString.CopyFrom(new byte[] { 1 }),
            }
        };
        ws.EnqueueReceive(WebSocketMessageType.Binary, frame.ToByteArray(), endOfMessage: true);

        var handler = CreateHandler(
            hook: new ThrowingHookClient(),
            dedup: new ThrowingDedup(),
            rateLimiter: new AllowRateLimiter());

        var session = new ConnectionSession("t1", "u1", "d1", "c1", "tr1", ws, new TenantRuntimeConfig { TenantMaxMessageQps = 0 });

        await InvokeReceiveLoopAsync(handler, session);

        var ack = ParseServerFrames(ws).Single(f => f.PayloadCase == ServerFrame.PayloadOneofCase.Ack).Ack;
        Assert.Equal("m1", ack.MsgId);
        Assert.Equal(AckStatus.Failed, ack.Status);
        Assert.Contains("to_user_id", ack.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Invalid_GroupChat_MissingGroupId_AcksFailed()
    {
        using var ws = new ScriptedWebSocket();

        var frame = new ClientFrame
        {
            Message = new MessageRequest
            {
                MsgId = "m2",
                MsgType = MessageType.GroupChat,
                MsgBody = ByteString.CopyFrom(new byte[] { 1 }),
            }
        };
        ws.EnqueueReceive(WebSocketMessageType.Binary, frame.ToByteArray(), endOfMessage: true);

        var handler = CreateHandler(
            hook: new ThrowingHookClient(),
            dedup: new ThrowingDedup(),
            rateLimiter: new AllowRateLimiter());

        var session = new ConnectionSession("t1", "u1", "d1", "c1", "tr1", ws, new TenantRuntimeConfig { TenantMaxMessageQps = 0 });

        await InvokeReceiveLoopAsync(handler, session);

        var ack = ParseServerFrames(ws).Single(f => f.PayloadCase == ServerFrame.PayloadOneofCase.Ack).Ack;
        Assert.Equal("m2", ack.MsgId);
        Assert.Equal(AckStatus.Failed, ack.Status);
        Assert.Contains("group_id", ack.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GroupChat_MembersOverLimit_AcksFailed()
    {
        using var ws = new ScriptedWebSocket();

        var frame = new ClientFrame
        {
            Message = new MessageRequest
            {
                MsgId = "m-group-too-large",
                MsgType = MessageType.GroupChat,
                GroupId = "g1",
                MsgBody = ByteString.CopyFrom(new byte[] { 1 }),
            }
        };
        ws.EnqueueReceive(WebSocketMessageType.Binary, frame.ToByteArray(), endOfMessage: true);

        var hook = new AllowGroupHookClient(new[] { "u1", "u2", "u3", "u4" });
        var metrics = new MetricsRegistry();
        var mq = new MqEventDispatcher(new NoopMqProducer(), metrics, TimeProvider.System, new MqEventDispatcherOptions(QueueCapacity: 1, MaxPendingPerTenant: 1, MaxAttempts: 1, RetryBackoffBase: TimeSpan.Zero, IdleDelay: TimeSpan.Zero));

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
            grpcBreaker: new GrpcNodeCircuitBreaker(TimeProvider.System),
            grpcBreakerPolicy: new GrpcBreakerPolicy(5, TimeSpan.FromSeconds(5)),
            offline: new NoopOffline(),
            rateLimiter: new AllowRateLimiter(),
            dedup: new AllowDedup(),
            mq: mq,
            metrics: metrics,
            logger: NullLogger<WsGatewayHandler>.Instance,
            traceContext: new TraceContext(),
            shutdown: new ShutdownState(),
            maxMessageBytes: 0,
            groupRouteChunkSize: 256,
            groupOfflineBufferMaxUsers: 1024,
            groupMembersMaxUsers: 3);

        var session = new ConnectionSession("t1", "u0", "d0", "c1", "tr1", ws, new TenantRuntimeConfig { TenantMaxMessageQps = 0 });

        await InvokeReceiveLoopAsync(handler, session);

        var ack = ParseServerFrames(ws).Single(f => f.PayloadCase == ServerFrame.PayloadOneofCase.Ack).Ack;
        Assert.Equal("m-group-too-large", ack.MsgId);
        Assert.Equal(AckStatus.Failed, ack.Status);
        Assert.Equal("group too large", ack.Reason);
    }

    [Fact]
    public async Task GroupChat_OfflineSkipped_IsReportedInAck()
    {
        using var ws = new ScriptedWebSocket();

        var frame = new ClientFrame
        {
            Message = new MessageRequest
            {
                MsgId = "m-group-offline",
                MsgType = MessageType.GroupChat,
                GroupId = "g1",
                MsgBody = ByteString.CopyFrom(new byte[] { 1 }),
            }
        };
        ws.EnqueueReceive(WebSocketMessageType.Binary, frame.ToByteArray(), endOfMessage: true);

        var hook = new AllowGroupHookClient(new[] { "u1", "u2", "u3", "u4", "u5" });
        var metrics = new MetricsRegistry();
        var mq = new MqEventDispatcher(new NoopMqProducer(), metrics, TimeProvider.System, new MqEventDispatcherOptions(QueueCapacity: 1, MaxPendingPerTenant: 1, MaxAttempts: 1, RetryBackoffBase: TimeSpan.Zero, IdleDelay: TimeSpan.Zero));

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
            grpcBreaker: new GrpcNodeCircuitBreaker(TimeProvider.System),
            grpcBreakerPolicy: new GrpcBreakerPolicy(5, TimeSpan.FromSeconds(5)),
            offline: new NoopOffline(),
            rateLimiter: new AllowRateLimiter(),
            dedup: new AllowDedup(),
            mq: mq,
            metrics: metrics,
            logger: NullLogger<WsGatewayHandler>.Instance,
            traceContext: new TraceContext(),
            shutdown: new ShutdownState(),
            maxMessageBytes: 0,
            groupRouteChunkSize: 256,
            groupOfflineBufferMaxUsers: 2,
            groupMembersMaxUsers: 1_000);

        var session = new ConnectionSession("t1", "u0", "d0", "c1", "tr1", ws, new TenantRuntimeConfig
        {
            TenantMaxMessageQps = 0,
            OfflineUseHookPull = false,
            OfflineBufferTtlSeconds = 60,
        });

        await InvokeReceiveLoopAsync(handler, session);

        var ack = ParseServerFrames(ws).Single(f => f.PayloadCase == ServerFrame.PayloadOneofCase.Ack).Ack;
        Assert.Equal("m-group-offline", ack.MsgId);
        Assert.Equal(AckStatus.Sent, ack.Status);
        Assert.True(ack.PartialDelivery);
        Assert.Equal((uint)0, ack.OfflineNotifiedUsers);
        Assert.Equal((uint)2, ack.OfflineBufferedUsers);
        Assert.Equal((uint)3, ack.OfflineSkippedUsers);
    }

    [Fact]
    public async Task SingleChat_WhenHookDenies_DoesNotDeliver_ButStillQueriesRoutes()
    {
        using var senderWs = new ScriptedWebSocket();
        using var receiverWs = new ScriptedWebSocket();

        var msg = new MessageRequest
        {
            MsgId = "m1",
            MsgType = MessageType.SingleChat,
            ToUserId = "u2",
            MsgBody = ByteString.CopyFrom(new byte[] { 1 }),
            TimestampMs = 1,
        };
        senderWs.EnqueueReceive(WebSocketMessageType.Binary, new ClientFrame { Message = msg }.ToByteArray(), endOfMessage: true);

        var connections = new ConnectionRegistry();
        var tenantCfg = new TenantRuntimeConfig { TenantMaxMessageQps = 0 };
        var sender = new ConnectionSession("t1", "u1", "d1", "c1", "tr1", senderWs, tenantCfg);
        var receiver = new ConnectionSession("t1", "u2", "d2", "c2", "tr2", receiverWs, tenantCfg);
        Assert.True(connections.TryAdd(sender));
        Assert.True(connections.TryAdd(receiver));

        var routes = new CountingRoutes(new Dictionary<string, OnlineDeviceRoute>(StringComparer.Ordinal)
        {
            ["d2"] = new OnlineDeviceRoute(NodeId: "node-1", Endpoint: "http://localhost:8080", ConnectionId: "c2", OnlineAtUnixMs: 1),
        });

        var handler = CreateHandler(
            hook: new DenyCheckHookClient(),
            connections: connections,
            routes: routes,
            dedup: new AllowDedup(),
            rateLimiter: new AllowRateLimiter());

        await InvokeReceiveLoopAsync(handler, sender);

        var senderAck = ParseServerFrames(senderWs).Single(f => f.PayloadCase == ServerFrame.PayloadOneofCase.Ack).Ack;
        Assert.Equal("m1", senderAck.MsgId);
        Assert.Equal(AckStatus.Failed, senderAck.Status);
        Assert.Contains("blocked", senderAck.Reason, StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain(ParseServerFrames(receiverWs), f => f.PayloadCase == ServerFrame.PayloadOneofCase.Delivery);
        Assert.Equal(1, routes.GetAllDevicesCalls);
    }

    [Fact]
    public async Task GroupChat_Deny_DoesNotAwaitMembersHook()
    {
        using var ws = new ScriptedWebSocket();

        var msg = new MessageRequest
        {
            MsgId = "m3",
            MsgType = MessageType.GroupChat,
            GroupId = "g1",
            MsgBody = ByteString.CopyFrom(new byte[] { 1 }),
            TimestampMs = 1,
        };
        ws.EnqueueReceive(WebSocketMessageType.Binary, new ClientFrame { Message = msg }.ToByteArray(), endOfMessage: true);

        var hook = new BlockingGroupHooks(allow: false, members: new[] { "u2" });
        var handler = CreateHandler(
            hook: hook,
            connections: new ConnectionRegistry(),
            routes: new NoopRoutes(),
            dedup: new AllowDedup(),
            rateLimiter: new AllowRateLimiter());

        var session = new ConnectionSession("t1", "u1", "d1", "c1", "tr1", ws, new TenantRuntimeConfig { TenantMaxMessageQps = 0 });

        var loop = InvokeReceiveLoopAsync(handler, session);

        await hook.MembersStarted.WaitAsync(TimeSpan.FromSeconds(1));
        hook.ReleaseCheck();

        await loop;

        var ack = ParseServerFrames(ws).Single(f => f.PayloadCase == ServerFrame.PayloadOneofCase.Ack).Ack;
        Assert.Equal("m3", ack.MsgId);
        Assert.Equal(AckStatus.Failed, ack.Status);
        Assert.Contains("blocked", ack.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GroupChat_StartsGetMembersBeforeCheckCompletes_AndStillGatesDelivery()
    {
        using var ws = new ScriptedWebSocket();

        var msg = new MessageRequest
        {
            MsgId = "m4",
            MsgType = MessageType.GroupChat,
            GroupId = "g1",
            MsgBody = ByteString.CopyFrom(new byte[] { 1 }),
            TimestampMs = 1,
        };
        ws.EnqueueReceive(WebSocketMessageType.Binary, new ClientFrame { Message = msg }.ToByteArray(), endOfMessage: true);

        var hook = new BlockingGroupHooks(allow: true, members: new[] { "u2" });
        var handler = CreateHandler(
            hook: hook,
            connections: new ConnectionRegistry(),
            routes: new NoopRoutes(),
            dedup: new AllowDedup(),
            rateLimiter: new AllowRateLimiter());

        var session = new ConnectionSession("t1", "u1", "d1", "c1", "tr1", ws, new TenantRuntimeConfig { TenantMaxMessageQps = 0 });

        var loop = InvokeReceiveLoopAsync(handler, session);

        await hook.MembersStarted.WaitAsync(TimeSpan.FromSeconds(1));
        hook.ReleaseMembers();
        hook.ReleaseCheck();

        await loop;

        var ack = ParseServerFrames(ws).Single(f => f.PayloadCase == ServerFrame.PayloadOneofCase.Ack).Ack;
        Assert.Equal("m4", ack.MsgId);
        Assert.Equal(AckStatus.Sent, ack.Status);
    }

    private static WsGatewayHandler CreateHandler(IHookClient hook, IMessageDeduplicator dedup, IRedisRateLimiter rateLimiter)
    {
        var metrics = new MetricsRegistry();
        var mq = new MqEventDispatcher(new NoopMqProducer(), metrics, TimeProvider.System, new MqEventDispatcherOptions(QueueCapacity: 1, MaxPendingPerTenant: 1, MaxAttempts: 1, RetryBackoffBase: TimeSpan.Zero, IdleDelay: TimeSpan.Zero));

        return new WsGatewayHandler(
            nodeId: "node-1",
            publicEndpoint: "http://localhost:8080",
            tenantAuthMap: new Dictionary<string, string>(StringComparer.Ordinal) { ["t1"] = "http://hook" },
            hook: hook,
            connections: new ConnectionRegistry(),
            routes: new NoopRoutes(),
            admission: new NoopAdmission(),
            nodes: new EmptyNodeSnapshot(),
            nodeClients: new NoopNodeClientPool(),
            grpcBreaker: new GrpcNodeCircuitBreaker(TimeProvider.System),
            grpcBreakerPolicy: new GrpcBreakerPolicy(5, TimeSpan.FromSeconds(5)),
            offline: new NoopOffline(),
            rateLimiter: rateLimiter,
            dedup: dedup,
            mq: mq,
            metrics: metrics,
            logger: NullLogger<WsGatewayHandler>.Instance,
            traceContext: new TraceContext(),
            shutdown: new ShutdownState(),
            maxMessageBytes: 0,
            groupRouteChunkSize: 256,
            groupOfflineBufferMaxUsers: 1024,
            groupMembersMaxUsers: 200_000);
    }

    private static WsGatewayHandler CreateHandler(IHookClient hook, IConnectionRegistry connections, IOnlineRouteStore routes, IMessageDeduplicator dedup, IRedisRateLimiter rateLimiter)
    {
        var metrics = new MetricsRegistry();
        var mq = new MqEventDispatcher(new NoopMqProducer(), metrics, TimeProvider.System, new MqEventDispatcherOptions(QueueCapacity: 1, MaxPendingPerTenant: 1, MaxAttempts: 1, RetryBackoffBase: TimeSpan.Zero, IdleDelay: TimeSpan.Zero));

        return new WsGatewayHandler(
            nodeId: "node-1",
            publicEndpoint: "http://localhost:8080",
            tenantAuthMap: new Dictionary<string, string>(StringComparer.Ordinal) { ["t1"] = "http://hook" },
            hook: hook,
            connections: connections,
            routes: routes,
            admission: new NoopAdmission(),
            nodes: new EmptyNodeSnapshot(),
            nodeClients: new NoopNodeClientPool(),
            grpcBreaker: new GrpcNodeCircuitBreaker(TimeProvider.System),
            grpcBreakerPolicy: new GrpcBreakerPolicy(5, TimeSpan.FromSeconds(5)),
            offline: new NoopOffline(),
            rateLimiter: rateLimiter,
            dedup: dedup,
            mq: mq,
            metrics: metrics,
            logger: NullLogger<WsGatewayHandler>.Instance,
            traceContext: new TraceContext(),
            shutdown: new ShutdownState(),
            maxMessageBytes: 0,
            groupRouteChunkSize: 256,
            groupOfflineBufferMaxUsers: 1024,
            groupMembersMaxUsers: 200_000);
    }

    private static async Task InvokeReceiveLoopAsync(WsGatewayHandler handler, ConnectionSession session)
    {
        var mi = typeof(WsGatewayHandler).GetMethod("ReceiveLoopAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(mi);
        var vt = (Task)mi!.Invoke(handler, new object[] { session, CancellationToken.None })!;
        await vt;
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
}
