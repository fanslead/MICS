using Google.Protobuf;
using Mics.Contracts.Hook.V1;
using Mics.Contracts.Message.V1;
using Mics.HookSdk;

namespace Mics.Tests;

public sealed class MqEventDecoderTests
{
    [Fact]
    public void TryDecodeConnectAck_ReturnsTrue_ForConnectEvents()
    {
        var ack = new ConnectAck { Code = 1000, TenantId = "t1", UserId = "u1", DeviceId = "d1", NodeId = "n1", TraceId = "tr1" };
        var evt = new MqEvent
        {
            TenantId = "t1",
            EventType = EventType.ConnectOnline,
            EventData = ByteString.CopyFrom(ack.ToByteArray()),
        };

        Assert.True(MqEventDecoder.TryDecodeConnectAck(evt, out var parsed));
        Assert.Equal(ack, parsed);
    }

    [Fact]
    public void TryDecodeMessage_ReturnsTrue_ForMessageEvents()
    {
        var msg = new MessageRequest { TenantId = "t1", UserId = "u1", DeviceId = "d1", MsgId = "m1", MsgType = MessageType.SingleChat, ToUserId = "u2" };
        var evt = new MqEvent
        {
            TenantId = "t1",
            EventType = EventType.SingleChatMsg,
            EventData = ByteString.CopyFrom(msg.ToByteArray()),
        };

        Assert.True(MqEventDecoder.TryDecodeMessage(evt, out var parsed));
        Assert.Equal(msg, parsed);
    }

    [Fact]
    public void TryVerifyAndDecodeMessage_ReturnsTrue_WhenSignValid()
    {
        var msg = new MessageRequest { TenantId = "t1", UserId = "u1", DeviceId = "d1", MsgId = "m1", MsgType = MessageType.SingleChat, ToUserId = "u2" };
        var evt = new MqEvent
        {
            TenantId = "t1",
            EventType = EventType.SingleChatMsg,
            MsgId = "m1",
            UserId = "u1",
            DeviceId = "d1",
            ToUserId = "u2",
            GroupId = "",
            EventData = ByteString.CopyFrom(msg.ToByteArray()),
            Timestamp = 1,
            NodeId = "n1",
            Sign = ""
        };
        var payload = evt.Clone();
        payload.Sign = "";
        evt.Sign = MqEventSigner.ComputeBase64("secret", payload);

        Assert.True(MqEventDecoder.TryVerifyAndDecodeMessage("secret", evt, out var parsed));
        Assert.Equal(msg, parsed);
    }
}

