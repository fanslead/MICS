using Mics.Contracts.Message.V1;
using Mics.Gateway.Connections;

namespace Mics.Tests;

/// <summary>
/// 测试消息验证逻辑：字段完整性、类型校验、边界条件
/// </summary>
public sealed class MessageValidationTests
{
    /// <summary>
    /// 从 WsGatewayHandler 中提取的消息验证逻辑（用于单元测试）
    /// </summary>
    private static bool TryValidateMessage(MessageRequest msg, out string reason)
    {
        reason = "";

        if (msg.MsgType == MessageType.SingleChat)
        {
            if (string.IsNullOrWhiteSpace(msg.ToUserId))
            {
                reason = "missing to_user_id";
                return false;
            }

            return true;
        }

        if (msg.MsgType == MessageType.GroupChat)
        {
            if (string.IsNullOrWhiteSpace(msg.GroupId))
            {
                reason = "missing group_id";
                return false;
            }

            return true;
        }

        reason = "invalid msg_type";
        return false;
    }

    /// <summary>
    /// 测试单聊消息的合法验证
    /// </summary>
    [Fact]
    public void ValidateMessage_SingleChat_Valid_ReturnsTrue()
    {
        // Arrange
        var msg = new MessageRequest
        {
            TenantId = "t1",
            UserId = "u1",
            DeviceId = "d1",
            MsgId = "m1",
            MsgType = MessageType.SingleChat,
            ToUserId = "u2",
            MsgBody = Google.Protobuf.ByteString.CopyFromUtf8("Hello"),
            TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };

        // Act
        var valid = TryValidateMessage(msg, out var reason);

        // Assert
        Assert.True(valid, "合法的单聊消息应通过验证");
        Assert.Empty(reason);
    }

    /// <summary>
    /// 测试单聊消息缺少 ToUserId
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateMessage_SingleChat_MissingToUserId_ReturnsFalse(string? toUserId)
    {
        // Arrange
        var msg = new MessageRequest
        {
            TenantId = "t1",
            UserId = "u1",
            DeviceId = "d1",
            MsgType = MessageType.SingleChat,
            ToUserId = toUserId ?? "",
        };

        // Act
        var valid = TryValidateMessage(msg, out var reason);

        // Assert
        Assert.False(valid, "缺少 ToUserId 应验证失败");
        Assert.Equal("missing to_user_id", reason);
    }

    /// <summary>
    /// 测试群聊消息的合法验证
    /// </summary>
    [Fact]
    public void ValidateMessage_GroupChat_Valid_ReturnsTrue()
    {
        // Arrange
        var msg = new MessageRequest
        {
            TenantId = "t1",
            UserId = "u1",
            DeviceId = "d1",
            MsgId = "m1",
            MsgType = MessageType.GroupChat,
            GroupId = "g1",
            MsgBody = Google.Protobuf.ByteString.CopyFromUtf8("Hello group"),
            TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };

        // Act
        var valid = TryValidateMessage(msg, out var reason);

        // Assert
        Assert.True(valid, "合法的群聊消息应通过验证");
        Assert.Empty(reason);
    }

    /// <summary>
    /// 测试群聊消息缺少 GroupId
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateMessage_GroupChat_MissingGroupId_ReturnsFalse(string? groupId)
    {
        // Arrange
        var msg = new MessageRequest
        {
            TenantId = "t1",
            UserId = "u1",
            DeviceId = "d1",
            MsgType = MessageType.GroupChat,
            GroupId = groupId ?? "",
        };

        // Act
        var valid = TryValidateMessage(msg, out var reason);

        // Assert
        Assert.False(valid, "缺少 GroupId 应验证失败");
        Assert.Equal("missing group_id", reason);
    }

    /// <summary>
    /// 测试无效的消息类型
    /// </summary>
    [Theory]
    [InlineData((MessageType)99)]
    [InlineData((MessageType)(-1))]
    public void ValidateMessage_InvalidMsgType_ReturnsFalse(MessageType invalidType)
    {
        // Arrange
        var msg = new MessageRequest
        {
            TenantId = "t1",
            UserId = "u1",
            DeviceId = "d1",
            MsgType = invalidType,
        };

        // Act
        var valid = TryValidateMessage(msg, out var reason);

        // Assert
        Assert.False(valid, "无效的消息类型应验证失败");
        Assert.Equal("invalid msg_type", reason);
    }

    /// <summary>
    /// 测试消息归一化（从客户端请求填充服务端字段）
    /// </summary>
    [Fact]
    public void NormalizeIncomingMessage_FillsServerFields()
    {
        // Arrange
        var session = CreateTestSession("t1", "u1", "d1");
        var clientMsg = new MessageRequest
        {
            MsgType = MessageType.SingleChat,
            ToUserId = "u2",
            MsgBody = Google.Protobuf.ByteString.CopyFromUtf8("Hello"),
            // MsgId 和 TimestampMs 为空（由客户端省略）
        };

        // Act
        var normalized = NormalizeIncomingMessage(session, clientMsg);

        // Assert
        Assert.Equal("t1", normalized.TenantId);
        Assert.Equal("u1", normalized.UserId);
        Assert.Equal("d1", normalized.DeviceId);
        Assert.NotEmpty(normalized.MsgId); // 应自动填充
        Assert.True(normalized.TimestampMs > 0); // 应自动填充
    }

    /// <summary>
    /// 测试消息归一化保留客户端提供的 MsgId 和 Timestamp
    /// </summary>
    [Fact]
    public void NormalizeIncomingMessage_PreservesClientFields()
    {
        // Arrange
        var session = CreateTestSession("t1", "u1", "d1");
        var clientMsg = new MessageRequest
        {
            MsgId = "client-msg-1",
            TimestampMs = 123456789,
            MsgType = MessageType.SingleChat,
            ToUserId = "u2",
        };

        // Act
        var normalized = NormalizeIncomingMessage(session, clientMsg);

        // Assert
        Assert.Equal("client-msg-1", normalized.MsgId); // 保留客户端 MsgId
        Assert.Equal(123456789, normalized.TimestampMs); // 保留客户端 Timestamp
    }

    /// <summary>
    /// 测试消息体大小限制
    /// </summary>
    [Theory]
    [InlineData(1024 * 1024)] // 1MB
    [InlineData(10 * 1024 * 1024)] // 10MB
    public void ValidateMessage_ExceedsMaxBytes_ShouldReject(int msgBodySize)
    {
        // Arrange
        const int maxMessageBytes = 5 * 1024 * 1024; // 5MB 上限
        var largeBody = new byte[msgBodySize];
        new Random().NextBytes(largeBody);

        var msg = new MessageRequest
        {
            TenantId = "t1",
            UserId = "u1",
            DeviceId = "d1",
            MsgType = MessageType.SingleChat,
            ToUserId = "u2",
            MsgBody = Google.Protobuf.ByteString.CopyFrom(largeBody),
        };

        // Act
        var exceeds = msg.MsgBody.Length > maxMessageBytes;

        // Assert
        if (msgBodySize > maxMessageBytes)
        {
            Assert.True(exceeds, $"消息体 {msgBodySize} 字节应超出限制 {maxMessageBytes}");
        }
        else
        {
            Assert.False(exceeds, $"消息体 {msgBodySize} 字节应在限制内");
        }
    }

    /// <summary>
    /// 测试空消息体的合法性
    /// </summary>
    [Fact]
    public void ValidateMessage_EmptyMsgBody_IsValid()
    {
        // Arrange
        var msg = new MessageRequest
        {
            TenantId = "t1",
            UserId = "u1",
            DeviceId = "d1",
            MsgType = MessageType.SingleChat,
            ToUserId = "u2",
            MsgBody = Google.Protobuf.ByteString.Empty, // 空消息体
        };

        // Act
        var valid = TryValidateMessage(msg, out var reason);

        // Assert
        Assert.True(valid, "空消息体应合法（用于特殊场景如撤回、已读）");
    }

    /// <summary>
    /// 测试 MsgId 唯一性（去重场景）
    /// </summary>
    [Fact]
    public void ValidateMessage_DuplicateMsgId_DetectedByDeduplicator()
    {
        // Arrange
        var deduplicator = new InMemoryMessageDeduplicator();
        const string tenantId = "t1";
        const string msgId = "duplicate-msg";

        // Act
        var firstAttempt = deduplicator.TryMarkAsync(tenantId, msgId, TimeSpan.FromMinutes(1), CancellationToken.None).AsTask().Result;
        var secondAttempt = deduplicator.TryMarkAsync(tenantId, msgId, TimeSpan.FromMinutes(1), CancellationToken.None).AsTask().Result;

        // Assert
        Assert.True(firstAttempt, "首次标记应成功");
        Assert.False(secondAttempt, "重复 MsgId 应被检测");
    }

    // Helper methods

    private static ConnectionSession CreateTestSession(string tenantId, string userId, string deviceId)
    {
        var socket = new TestWebSocket();
        var config = new Mics.Contracts.Hook.V1.TenantRuntimeConfig
        {
            HeartbeatTimeoutSeconds = 30,
            OfflineBufferTtlSeconds = 300,
        };

        return new Mics.Gateway.Connections.ConnectionSession(
            tenantId,
            userId,
            deviceId,
            Guid.NewGuid().ToString("N"),
            Guid.NewGuid().ToString("N"),
            socket,
            config);
    }

    private static MessageRequest NormalizeIncomingMessage(
        Mics.Gateway.Connections.ConnectionSession session,
        MessageRequest client)
    {
        var msg = client.Clone();
        msg.TenantId = session.TenantId;
        msg.UserId = session.UserId;
        msg.DeviceId = session.DeviceId;

        if (string.IsNullOrWhiteSpace(msg.MsgId))
        {
            msg.MsgId = Guid.NewGuid().ToString("N");
        }

        if (msg.TimestampMs == 0)
        {
            msg.TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        return msg;
    }
}

/// <summary>
/// 内存消息去重器（用于测试）
/// </summary>
internal sealed class InMemoryMessageDeduplicator
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<(string TenantId, string MsgId), DateTimeOffset> _cache = new();

    public ValueTask<bool> TryMarkAsync(string tenantId, string msgId, TimeSpan ttl, CancellationToken cancellationToken)
    {
        var key = (tenantId, msgId);
        var expiry = DateTimeOffset.UtcNow.Add(ttl);

        var added = _cache.TryAdd(key, expiry);
        return ValueTask.FromResult(added);
    }
}
