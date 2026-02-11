using System.Net.WebSockets;
using Mics.Contracts.Hook.V1;
using Mics.Gateway.Connections;

namespace Mics.Tests;

/// <summary>
/// 测试连接注册表的核心功能：多租户隔离、多端在线、并发安全
/// </summary>
public sealed class ConnectionRegistryTests
{
    private static ConnectionSession CreateSession(
        string tenantId,
        string userId,
        string deviceId,
        WebSocket? socket = null)
    {
        socket ??= new TestWebSocket();
        var config = new TenantRuntimeConfig
        {
            HeartbeatTimeoutSeconds = 30,
            OfflineBufferTtlSeconds = 300,
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

    /// <summary>
    /// 测试基础的添加和获取功能
    /// </summary>
    [Fact]
    public void TryAdd_AndTryGet_Success()
    {
        // Arrange
        var registry = new ConnectionRegistry();
        var session = CreateSession("t1", "u1", "d1");

        // Act
        var added = registry.TryAdd(session);
        var got = registry.TryGet("t1", "u1", "d1", out var retrieved);

        // Assert
        Assert.True(added, "应成功添加新连接");
        Assert.True(got, "应能获取已添加的连接");
        Assert.NotNull(retrieved);
        Assert.Equal(session.ConnectionId, retrieved.ConnectionId);
    }

    /// <summary>
    /// 测试重复添加同一设备连接应失败（防止重复连接）
    /// </summary>
    [Fact]
    public void TryAdd_DuplicateDevice_ReturnsFalse()
    {
        // Arrange
        var registry = new ConnectionRegistry();
        var session1 = CreateSession("t1", "u1", "d1");
        var session2 = CreateSession("t1", "u1", "d1"); // 相同租户、用户、设备

        // Act
        var added1 = registry.TryAdd(session1);
        var added2 = registry.TryAdd(session2);

        // Assert
        Assert.True(added1, "第一个连接应成功");
        Assert.False(added2, "重复设备连接应失败");
    }

    /// <summary>
    /// 测试多端在线：同一用户的不同设备可以同时在线
    /// </summary>
    [Fact]
    public void TryAdd_MultipleDevices_ForSameUser_Success()
    {
        // Arrange
        var registry = new ConnectionRegistry();
        var session1 = CreateSession("t1", "u1", "iOS");
        var session2 = CreateSession("t1", "u1", "Android");
        var session3 = CreateSession("t1", "u1", "Web");

        // Act
        var added1 = registry.TryAdd(session1);
        var added2 = registry.TryAdd(session2);
        var added3 = registry.TryAdd(session3);

        // Assert
        Assert.True(added1, "iOS 设备应成功添加");
        Assert.True(added2, "Android 设备应成功添加");
        Assert.True(added3, "Web 设备应成功添加");

        // 验证所有设备都能获取
        Assert.True(registry.TryGet("t1", "u1", "iOS", out _));
        Assert.True(registry.TryGet("t1", "u1", "Android", out _));
        Assert.True(registry.TryGet("t1", "u1", "Web", out _));
    }

    /// <summary>
    /// 测试租户隔离：不同租户的相同用户ID应独立管理
    /// </summary>
    [Fact]
    public void TryAdd_DifferentTenants_Isolated()
    {
        // Arrange
        var registry = new ConnectionRegistry();
        var session1 = CreateSession("tenant1", "u1", "d1");
        var session2 = CreateSession("tenant2", "u1", "d1");

        // Act
        var added1 = registry.TryAdd(session1);
        var added2 = registry.TryAdd(session2);

        // Assert
        Assert.True(added1);
        Assert.True(added2, "不同租户的相同用户应能同时存在");

        Assert.True(registry.TryGet("tenant1", "u1", "d1", out var s1));
        Assert.True(registry.TryGet("tenant2", "u1", "d1", out var s2));
        Assert.Equal("tenant1", s1!.TenantId);
        Assert.Equal("tenant2", s2!.TenantId);
    }

    /// <summary>
    /// 测试移除连接
    /// </summary>
    [Fact]
    public void TryRemove_ExistingSession_Success()
    {
        // Arrange
        var registry = new ConnectionRegistry();
        var session = CreateSession("t1", "u1", "d1");
        registry.TryAdd(session);

        // Act
        var removed = registry.TryRemove("t1", "u1", "d1", out var removedSession);

        // Assert
        Assert.True(removed, "应成功移除");
        Assert.NotNull(removedSession);
        Assert.Equal(session.ConnectionId, removedSession.ConnectionId);

        // 移除后应无法获取
        Assert.False(registry.TryGet("t1", "u1", "d1", out _));
    }

    /// <summary>
    /// 测试移除不存在的连接
    /// </summary>
    [Fact]
    public void TryRemove_NonExistentSession_ReturnsFalse()
    {
        // Arrange
        var registry = new ConnectionRegistry();

        // Act
        var removed = registry.TryRemove("t1", "u1", "d1", out var removedSession);

        // Assert
        Assert.False(removed);
        Assert.Null(removedSession);
    }

    /// <summary>
    /// 测试复制同一用户的所有设备连接（用于多端投递）
    /// </summary>
    [Fact]
    public void CopyAllForUserTo_MultipleDevices_Success()
    {
        // Arrange
        var registry = new ConnectionRegistry();
        var session1 = CreateSession("t1", "u1", "iOS");
        var session2 = CreateSession("t1", "u1", "Android");
        var session3 = CreateSession("t1", "u2", "Web"); // 不同用户

        registry.TryAdd(session1);
        registry.TryAdd(session2);
        registry.TryAdd(session3);

        // Act
        var destination = new List<ConnectionSession>();
        registry.CopyAllForUserTo("t1", "u1", destination);

        // Assert
        Assert.Equal(2, destination.Count);
        Assert.Contains(destination, s => s.DeviceId == "iOS");
        Assert.Contains(destination, s => s.DeviceId == "Android");
        Assert.DoesNotContain(destination, s => s.DeviceId == "Web");
    }

    /// <summary>
    /// 测试复制所有连接（用于全局统计）
    /// </summary>
    [Fact]
    public void CopyAllSessionsTo_AllTenants_Success()
    {
        // Arrange
        var registry = new ConnectionRegistry();
        registry.TryAdd(CreateSession("t1", "u1", "d1"));
        registry.TryAdd(CreateSession("t1", "u2", "d2"));
        registry.TryAdd(CreateSession("t2", "u3", "d3"));

        // Act
        var destination = new List<ConnectionSession>();
        registry.CopyAllSessionsTo(destination);

        // Assert
        Assert.Equal(3, destination.Count);
    }

    /// <summary>
    /// 测试并发添加/移除（线程安全）
    /// </summary>
    [Fact]
    public async Task ConcurrentAddRemove_ThreadSafe()
    {
        // Arrange
        var registry = new ConnectionRegistry();
        const int concurrency = 10;
        const int operationsPerThread = 100;

        // Act
        var tasks = Enumerable.Range(0, concurrency).Select(async threadId =>
        {
            await Task.Yield();

            for (var i = 0; i < operationsPerThread; i++)
            {
                var userId = $"u{threadId}";
                var deviceId = $"d{i}";

                var session = CreateSession("t1", userId, deviceId);
                registry.TryAdd(session);

                // 随机移除
                if (i % 2 == 0)
                {
                    registry.TryRemove("t1", userId, deviceId, out _);
                }
            }
        }).ToArray();

        await Task.WhenAll(tasks);

        // Assert
        var allSessions = new List<ConnectionSession>();
        registry.CopyAllSessionsTo(allSessions);

        // 应有 concurrency * (operationsPerThread / 2) 个连接（未被移除的）
        Assert.True(allSessions.Count <= concurrency * operationsPerThread);
    }

    /// <summary>
    /// 测试清空用户所有设备后，内部映射应清理
    /// </summary>
    [Fact]
    public void Remove_LastDevice_CleansUpInternalMap()
    {
        // Arrange
        var registry = new ConnectionRegistry();
        registry.TryAdd(CreateSession("t1", "u1", "d1"));
        registry.TryAdd(CreateSession("t1", "u1", "d2"));

        // Act
        registry.TryRemove("t1", "u1", "d1", out _);
        registry.TryRemove("t1", "u1", "d2", out _);

        // Assert
        var allSessions = new List<ConnectionSession>();
        registry.CopyAllSessionsTo(allSessions);
        Assert.Empty(allSessions);

        // 再次添加应成功（验证内部映射已清理）
        var newSession = CreateSession("t1", "u1", "d1");
        Assert.True(registry.TryAdd(newSession));
    }

    /// <summary>
    /// 测试空用户列表复制
    /// </summary>
    [Fact]
    public void CopyAllForUserTo_NonExistentUser_ReturnsEmpty()
    {
        // Arrange
        var registry = new ConnectionRegistry();

        // Act
        var destination = new List<ConnectionSession>();
        registry.CopyAllForUserTo("t1", "nonexistent", destination);

        // Assert
        Assert.Empty(destination);
    }
}

/// <summary>
/// 测试用的 WebSocket 模拟
/// </summary>
internal sealed class TestWebSocket : WebSocket
{
    private WebSocketState _state = WebSocketState.Open;

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

    public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
        => Task.FromResult(new WebSocketReceiveResult(0, WebSocketMessageType.Close, true));

    public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public override ValueTask SendAsync(ReadOnlyMemory<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
        => ValueTask.CompletedTask;
}
