using System.Collections.Concurrent;
using Mics.Contracts.Hook.V1;
using Mics.Contracts.Message.V1;
using Mics.Gateway.Connections;
using Mics.Gateway.Hook;
using Mics.Gateway.Infrastructure.Redis;
using Mics.Gateway.Metrics;
using Mics.Gateway.Mq;

namespace Mics.Tests;

/// <summary>
/// 端到端集成测试：覆盖完整的消息发送流程（鉴权 → 路由 → Hook 校验 → 投递 → MQ）
/// 注意：这是简化的集成测试，实际的完整集成测试应使用真实 Redis/Kafka
/// </summary>
public sealed class EndToEndMessageFlowTests
{
    /// <summary>
    /// 测试单聊消息完整流程（模拟）
    /// </summary>
    [Fact]
    public async Task SingleChatFlow_HappyPath_Success()
    {
        // Arrange - 模拟组件
        var connectionRegistry = new ConnectionRegistry();
        var onlineRouteStore = new MockOnlineRouteStore();
        var hookClient = new MockHookClient(allowMessages: true);
        var mqDispatcher = new MockMqDispatcher();
        var messageDeduplicator = new InMemoryMessageDeduplicator();

        // 建立发送者连接
        var senderSession = CreateSession("t1", "u1", "d1");
        connectionRegistry.TryAdd(senderSession);

        // 建立接收者连接（本地节点）
        var receiverSession = CreateSession("t1", "u2", "d2");
        connectionRegistry.TryAdd(receiverSession);

        // 注册接收者在线路由
        await onlineRouteStore.UpsertAsync(
            "t1",
            "u2",
            "d2",
            new OnlineDeviceRoute("node-local", "http://localhost:8080", receiverSession.ConnectionId, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()),
            CancellationToken.None);

        // Act - 模拟发送单聊消息
        var message = new MessageRequest
        {
            TenantId = "t1",
            UserId = "u1",
            DeviceId = "d1",
            MsgId = "msg-1",
            MsgType = MessageType.SingleChat,
            ToUserId = "u2",
            MsgBody = Google.Protobuf.ByteString.CopyFromUtf8("Hello u2!"),
            TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };

        // 1. 消息去重
        var isNew = await messageDeduplicator.TryMarkAsync("t1", "msg-1", TimeSpan.FromMinutes(10), CancellationToken.None);
        Assert.True(isNew, "首次消息应通过去重");

        // 2. Hook 校验
        var hookResult = await hookClient.CheckMessageAsync(
            senderSession.TenantConfig,
            "t1",
            message,
            CancellationToken.None);

        Assert.True(hookResult.Allow, "Hook 应允许消息");

        // 3. 查询接收者路由
        var routes = await onlineRouteStore.GetAllDevicesAsync("t1", "u2", CancellationToken.None);
        Assert.Single(routes);

        // 4. 本地投递（模拟）
        var deliveredMessages = new List<MessageRequest>();
        var destination = new List<ConnectionSession>();
        connectionRegistry.CopyAllForUserTo("t1", "u2", destination);

        foreach (var session in destination)
        {
            // 模拟 WebSocket 发送
            deliveredMessages.Add(message);
        }

        // 5. MQ 投递
        var mqEvent = MqEventFactory.CreateForMessage(
            message,
            "node-local",
            senderSession.TraceId,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            senderSession.TenantConfig.TenantSecret);

        mqDispatcher.TryEnqueue(mqEvent);

        // Assert
        Assert.NotEmpty(deliveredMessages);
        Assert.Equal("msg-1", deliveredMessages[0].MsgId);
        Assert.Single(mqDispatcher.EnqueuedEvents);
        Assert.Equal(EventType.SingleChatMsg, mqDispatcher.EnqueuedEvents[0].EventType);
    }

    /// <summary>
    /// 测试 Hook 拒绝消息的流程
    /// </summary>
    [Fact]
    public async Task SingleChatFlow_HookDenies_MessageRejected()
    {
        // Arrange
        var hookClient = new MockHookClient(allowMessages: false, reason: "敏感词拦截");

        var message = new MessageRequest
        {
            TenantId = "t1",
            UserId = "u1",
            DeviceId = "d1",
            MsgId = "msg-sensitive",
            MsgType = MessageType.SingleChat,
            ToUserId = "u2",
            MsgBody = Google.Protobuf.ByteString.CopyFromUtf8("敏感内容"),
        };

        var tenantConfig = new TenantRuntimeConfig
        {
            HookBaseUrl = "http://hook:8080",
            TenantSecret = "secret",
        };

        // Act
        var hookResult = await hookClient.CheckMessageAsync(tenantConfig, "t1", message, CancellationToken.None);

        // Assert
        Assert.False(hookResult.Allow, "Hook 应拒绝敏感消息");
        Assert.Equal("敏感词拦截", hookResult.Reason);
    }

    /// <summary>
    /// 测试接收者离线时的离线缓冲流程
    /// </summary>
    [Fact]
    public async Task SingleChatFlow_ReceiverOffline_Buffered()
    {
        // Arrange
        var onlineRouteStore = new MockOnlineRouteStore();
        var offlineBufferStore = new Mics.Gateway.Offline.OfflineBufferStore(
            maxMessagesPerUser: 100,
            maxBytesPerUser: 10 * 1024 * 1024);

        // 接收者离线（无路由）
        var routes = await onlineRouteStore.GetAllDevicesAsync("t1", "u2-offline", CancellationToken.None);
        Assert.Empty(routes);

        // Act - 缓冲离线消息
        var frameBytes = Google.Protobuf.ByteString.CopyFromUtf8("offline-message");
        var buffered = offlineBufferStore.TryAdd(
            "t1",
            "u2-offline",
            frameBytes,
            TimeSpan.FromMinutes(5));

        // Assert
        Assert.True(buffered, "离线消息应成功缓冲");

        // 模拟用户上线后拉取
        var drained = offlineBufferStore.Drain("t1", "u2-offline").ToArray();
        Assert.Single(drained);
        Assert.Equal(frameBytes, drained[0]);
    }

    /// <summary>
    /// 测试群聊消息扇出流程（模拟）
    /// </summary>
    [Fact]
    public async Task GroupChatFlow_MultipleMembers_FanoutSuccess()
    {
        // Arrange
        var connectionRegistry = new ConnectionRegistry();
        var onlineRouteStore = new MockOnlineRouteStore();
        var hookClient = new MockHookClient(allowMessages: true);

        // 群成员: u1, u2, u3（其中 u3 离线）
        var u1Session = CreateSession("t1", "u1", "d1");
        var u2Session = CreateSession("t1", "u2", "d2");

        connectionRegistry.TryAdd(u1Session);
        connectionRegistry.TryAdd(u2Session);

        await onlineRouteStore.UpsertAsync("t1", "u1", "d1",
            new OnlineDeviceRoute("node-local", "http://localhost:8080", u1Session.ConnectionId, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()),
            CancellationToken.None);

        await onlineRouteStore.UpsertAsync("t1", "u2", "d2",
            new OnlineDeviceRoute("node-local", "http://localhost:8080", u2Session.ConnectionId, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()),
            CancellationToken.None);

        // 配置 Hook 返回群成员
        hookClient.SetGroupMembers("g1", new[] { "u1", "u2", "u3" });

        var groupMessage = new MessageRequest
        {
            TenantId = "t1",
            UserId = "u1",
            DeviceId = "d1",
            MsgId = "group-msg-1",
            MsgType = MessageType.GroupChat,
            GroupId = "g1",
            MsgBody = Google.Protobuf.ByteString.CopyFromUtf8("Hello group!"),
            TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };

        // Act
        // 1. Hook 校验
        var hookResult = await hookClient.CheckMessageAsync(u1Session.TenantConfig, "t1", groupMessage, CancellationToken.None);
        Assert.True(hookResult.Allow);

        // 2. 获取群成员
        var members = await hookClient.GetGroupMembersAsync(u1Session.TenantConfig, "t1", "g1", CancellationToken.None);
        Assert.True(members.Ok);
        Assert.Equal(3, members.UserIds.Count);

        // 3. 查询成员路由
        var routesByUser = await onlineRouteStore.GetAllDevicesForUsersAsync("t1", members.UserIds, CancellationToken.None);

        // Assert
        Assert.Equal(2, routesByUser.Count); // u1, u2 在线
        Assert.True(routesByUser.ContainsKey("u1"));
        Assert.True(routesByUser.ContainsKey("u2"));
        Assert.False(routesByUser.ContainsKey("u3")); // u3 离线
    }

    /// <summary>
    /// 测试消息重复去重
    /// </summary>
    [Fact]
    public async Task MessageDeduplication_DuplicateMessage_Rejected()
    {
        // Arrange
        var deduplicator = new InMemoryMessageDeduplicator();
        const string tenantId = "t1";
        const string msgId = "duplicate-msg";
        var ttl = TimeSpan.FromMinutes(10);

        // Act
        var attempt1 = await deduplicator.TryMarkAsync(tenantId, msgId, ttl, CancellationToken.None);
        var attempt2 = await deduplicator.TryMarkAsync(tenantId, msgId, ttl, CancellationToken.None);
        var attempt3 = await deduplicator.TryMarkAsync(tenantId, "other-msg", ttl, CancellationToken.None);

        // Assert
        Assert.True(attempt1, "首次消息应通过");
        Assert.False(attempt2, "重复消息应拒绝");
        Assert.True(attempt3, "不同消息应通过");
    }

    // Helper classes

    private static ConnectionSession CreateSession(string tenantId, string userId, string deviceId)
    {
        var socket = new TestWebSocket();
        var config = new TenantRuntimeConfig
        {
            HookBaseUrl = "http://hook:8080",
            HeartbeatTimeoutSeconds = 30,
            OfflineBufferTtlSeconds = 300,
            TenantSecret = "test-secret",
        };

        return new ConnectionSession(
            tenantId,
            userId,
            deviceId,
            Guid.NewGuid().ToString("N"),
            Guid.NewGuid().ToString("N"),
            socket,
            config);
    }
}

/// <summary>
/// Mock 在线路由存储
/// </summary>
internal sealed class MockOnlineRouteStore : IOnlineRouteStore
{
    private readonly ConcurrentDictionary<(string TenantId, string UserId, string DeviceId), OnlineDeviceRoute> _routes = new();

    public ValueTask UpsertAsync(string tenantId, string userId, string deviceId, OnlineDeviceRoute route, CancellationToken cancellationToken)
    {
        _routes[(tenantId, userId, deviceId)] = route;
        return ValueTask.CompletedTask;
    }

    public ValueTask RemoveAsync(string tenantId, string userId, string deviceId, CancellationToken cancellationToken)
    {
        _routes.TryRemove((tenantId, userId, deviceId), out _);
        return ValueTask.CompletedTask;
    }

    public ValueTask<IReadOnlyDictionary<string, OnlineDeviceRoute>> GetAllDevicesAsync(string tenantId, string userId, CancellationToken cancellationToken)
    {
        var result = _routes
            .Where(kv => kv.Key.TenantId == tenantId && kv.Key.UserId == userId)
            .ToDictionary(kv => kv.Key.DeviceId, kv => kv.Value, StringComparer.Ordinal);

        return ValueTask.FromResult<IReadOnlyDictionary<string, OnlineDeviceRoute>>(result);
    }

    public ValueTask<IReadOnlyDictionary<string, IReadOnlyList<OnlineDeviceRoute>>> GetAllDevicesForUsersAsync(
        string tenantId,
        IReadOnlyList<string> userIds,
        CancellationToken cancellationToken)
    {
        var result = userIds
            .Select(userId => (userId, routes: _routes
                .Where(kv => kv.Key.TenantId == tenantId && kv.Key.UserId == userId)
                .Select(kv => kv.Value)
                .ToArray()))
            .Where(tuple => tuple.routes.Length > 0)
            .ToDictionary(tuple => tuple.userId, tuple => (IReadOnlyList<OnlineDeviceRoute>)tuple.routes, StringComparer.Ordinal);

        return ValueTask.FromResult<IReadOnlyDictionary<string, IReadOnlyList<OnlineDeviceRoute>>>(result);
    }

    public ValueTask<long> GetDeviceCountAsync(string tenantId, string userId, CancellationToken cancellationToken)
    {
        var count = _routes.Count(kv => kv.Key.TenantId == tenantId && kv.Key.UserId == userId);
        return ValueTask.FromResult((long)count);
    }
}

/// <summary>
/// Mock Hook 客户端
/// </summary>
internal sealed class MockHookClient : IHookClient
{
    private readonly bool _allowMessages;
    private readonly string _reason;
    private readonly Dictionary<string, string[]> _groupMembers = new();

    public MockHookClient(bool allowMessages, string reason = "")
    {
        _allowMessages = allowMessages;
        _reason = reason;
    }

    public void SetGroupMembers(string groupId, string[] members)
    {
        _groupMembers[groupId] = members;
    }

    public ValueTask<AuthResult> AuthAsync(string authHookBaseUrl, string tenantId, string token, string deviceId, CancellationToken cancellationToken)
    {
        var config = new TenantRuntimeConfig
        {
            HookBaseUrl = authHookBaseUrl,
            HeartbeatTimeoutSeconds = 30,
            OfflineBufferTtlSeconds = 300,
            TenantSecret = "mock-secret",
        };

        return ValueTask.FromResult(new AuthResult(true, "u1", deviceId, config, ""));
    }

    public ValueTask<CheckMessageResult> CheckMessageAsync(TenantRuntimeConfig tenantConfig, string tenantId, MessageRequest message, CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(new CheckMessageResult(_allowMessages, false, _reason));
    }

    public ValueTask<GroupMembersResult> GetGroupMembersAsync(TenantRuntimeConfig tenantConfig, string tenantId, string groupId, CancellationToken cancellationToken)
    {
        if (_groupMembers.TryGetValue(groupId, out var members))
        {
            return ValueTask.FromResult(new GroupMembersResult(true, false, "", members));
        }

        return ValueTask.FromResult(new GroupMembersResult(false, false, "group not found", Array.Empty<string>()));
    }

    public ValueTask<GetOfflineMessagesResult> GetOfflineMessagesAsync(
        TenantRuntimeConfig tenantConfig,
        string tenantId,
        string userId,
        string deviceId,
        int maxMessages,
        string cursor,
        CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(new GetOfflineMessagesResult(true, false, "", Array.Empty<MessageRequest>(), "", false));
    }
}

/// <summary>
/// Mock MQ 分发器
/// </summary>
internal sealed class MockMqDispatcher
{
    public List<MqEvent> EnqueuedEvents { get; } = new();

    public bool TryEnqueue(MqEvent evt)
    {
        EnqueuedEvents.Add(evt);
        return true;
    }
}
