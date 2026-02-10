using Google.Protobuf;
using Mics.Contracts.Hook.V1;
using Mics.Contracts.Message.V1;
using Mics.Gateway.Mq;
using Mics.Gateway.Security;

namespace Mics.Tests;

public sealed class MqEventFactoryTests
{
    [Fact]
    public void CreateConnectOnline_SetsFields_AndUsesConnectAckAsEventData()
    {
        var evt = MqEventFactory.CreateConnectOnline(
            tenantId: "t1",
            userId: "u1",
            deviceId: "d1",
            nodeId: "n1",
            traceId: "tr1",
            unixTimestamp: 123,
            tenantSecret: "secret");

        Assert.Equal("t1", evt.TenantId);
        Assert.Equal(EventType.ConnectOnline, evt.EventType);
        Assert.Equal("u1", evt.UserId);
        Assert.Equal("d1", evt.DeviceId);
        Assert.Equal("n1", evt.NodeId);
        Assert.Equal("tr1", evt.TraceId);
        Assert.Equal(123, evt.Timestamp);
        Assert.NotEmpty(evt.EventData);
        Assert.False(string.IsNullOrWhiteSpace(evt.Sign));

        var payload = evt.Clone();
        payload.Sign = "";
        Assert.Equal(HmacSign.ComputeBase64("secret", payload), evt.Sign);

        var ack = ConnectAck.Parser.ParseFrom(evt.EventData);
        Assert.Equal(1000, ack.Code);
        Assert.Equal("t1", ack.TenantId);
        Assert.Equal("u1", ack.UserId);
        Assert.Equal("d1", ack.DeviceId);
        Assert.Equal("n1", ack.NodeId);
        Assert.Equal("tr1", ack.TraceId);
    }

    [Fact]
    public void CreateForMessage_SerializesOriginalMessage()
    {
        var msg = new MessageRequest
        {
            TenantId = "t1",
            UserId = "u1",
            DeviceId = "d1",
            MsgId = "m1",
            MsgType = MessageType.SingleChat,
            ToUserId = "u2",
            GroupId = "",
            MsgBody = ByteString.CopyFrom(new byte[] { 1, 2, 3 }),
            TimestampMs = 999,
        };

        var evt = MqEventFactory.CreateForMessage(msg, nodeId: "n1", traceId: "tr1", unixTimestamp: 123, tenantSecret: "secret");
        Assert.Equal(EventType.SingleChatMsg, evt.EventType);
        Assert.Equal("m1", evt.MsgId);
        Assert.Equal("t1", evt.TenantId);
        Assert.Equal("u1", evt.UserId);
        Assert.Equal("d1", evt.DeviceId);
        Assert.Equal("u2", evt.ToUserId);
        Assert.Equal("", evt.GroupId);
        Assert.Equal("n1", evt.NodeId);
        Assert.Equal("tr1", evt.TraceId);
        Assert.Equal(123, evt.Timestamp);
        Assert.False(string.IsNullOrWhiteSpace(evt.Sign));

        var payload = evt.Clone();
        payload.Sign = "";
        Assert.Equal(HmacSign.ComputeBase64("secret", payload), evt.Sign);

        var parsed = MessageRequest.Parser.ParseFrom(evt.EventData);
        Assert.Equal(msg, parsed);
    }

    [Fact]
    public void CreateOfflineMessage_SerializesOriginalMessage_AndSetsOfflineEventType()
    {
        var msg = new MessageRequest
        {
            TenantId = "t1",
            UserId = "u1",
            DeviceId = "d1",
            MsgId = "m1",
            MsgType = MessageType.SingleChat,
            ToUserId = "u2",
            GroupId = "",
            MsgBody = ByteString.CopyFrom(new byte[] { 1, 2, 3 }),
            TimestampMs = 999,
        };

        var evt = MqEventFactory.CreateOfflineMessage(msg, nodeId: "n1", traceId: "tr1", unixTimestamp: 123, tenantSecret: "secret");
        Assert.Equal(EventType.OfflineMessage, evt.EventType);
        Assert.Equal("m1", evt.MsgId);
        Assert.Equal("t1", evt.TenantId);
        Assert.Equal("u1", evt.UserId);
        Assert.Equal("d1", evt.DeviceId);
        Assert.Equal("u2", evt.ToUserId);
        Assert.Equal("", evt.GroupId);
        Assert.Equal("n1", evt.NodeId);
        Assert.Equal("tr1", evt.TraceId);
        Assert.Equal(123, evt.Timestamp);
        Assert.False(string.IsNullOrWhiteSpace(evt.Sign));

        var payload = evt.Clone();
        payload.Sign = "";
        Assert.Equal(HmacSign.ComputeBase64("secret", payload), evt.Sign);

        var parsed = MessageRequest.Parser.ParseFrom(evt.EventData);
        Assert.Equal(msg, parsed);
    }
}
