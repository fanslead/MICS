using Google.Protobuf;
using Mics.Contracts.Hook.V1;
using Mics.Contracts.Message.V1;
using Mics.Gateway.Security;

namespace Mics.Gateway.Mq;

internal static class MqEventFactory
{
    public static MqEvent CreateConnectOnline(string tenantId, string userId, string deviceId, string nodeId, string traceId, long unixTimestamp, string tenantSecret)
    {
        var ack = new ConnectAck
        {
            Code = 1000,
            TenantId = tenantId,
            UserId = userId,
            DeviceId = deviceId,
            NodeId = nodeId,
            TraceId = traceId,
        };

        var evt = new MqEvent
        {
            TenantId = tenantId,
            EventType = EventType.ConnectOnline,
            MsgId = "",
            UserId = userId,
            DeviceId = deviceId,
            ToUserId = "",
            GroupId = "",
            EventData = ByteString.CopyFrom(ack.ToByteArray()),
            Timestamp = unixTimestamp,
            NodeId = nodeId,
            TraceId = traceId,
        };

        SignIfPossible(evt, tenantSecret);
        return evt;
    }

    public static MqEvent CreateConnectOffline(string tenantId, string userId, string deviceId, string nodeId, string traceId, long unixTimestamp, string tenantSecret)
    {
        var ack = new ConnectAck
        {
            Code = 1000,
            TenantId = tenantId,
            UserId = userId,
            DeviceId = deviceId,
            NodeId = nodeId,
            TraceId = traceId,
        };

        var evt = new MqEvent
        {
            TenantId = tenantId,
            EventType = EventType.ConnectOffline,
            MsgId = "",
            UserId = userId,
            DeviceId = deviceId,
            ToUserId = "",
            GroupId = "",
            EventData = ByteString.CopyFrom(ack.ToByteArray()),
            Timestamp = unixTimestamp,
            NodeId = nodeId,
            TraceId = traceId,
        };

        SignIfPossible(evt, tenantSecret);
        return evt;
    }

    public static MqEvent CreateForMessage(MessageRequest message, string nodeId, string traceId, long unixTimestamp, string tenantSecret)
    {
        ArgumentNullException.ThrowIfNull(message);

        var evtType = message.MsgType == MessageType.GroupChat
            ? EventType.GroupChatMsg
            : EventType.SingleChatMsg;

        var evt = new MqEvent
        {
            TenantId = message.TenantId ?? "",
            EventType = evtType,
            MsgId = message.MsgId ?? "",
            UserId = message.UserId ?? "",
            DeviceId = message.DeviceId ?? "",
            ToUserId = message.ToUserId ?? "",
            GroupId = message.GroupId ?? "",
            EventData = ByteString.CopyFrom(message.ToByteArray()),
            Timestamp = unixTimestamp,
            NodeId = nodeId,
            TraceId = traceId ?? "",
        };

        SignIfPossible(evt, tenantSecret);
        return evt;
    }

    public static MqEvent CreateOfflineMessage(MessageRequest message, string nodeId, string traceId, long unixTimestamp, string tenantSecret)
    {
        ArgumentNullException.ThrowIfNull(message);

        var evt = new MqEvent
        {
            TenantId = message.TenantId ?? "",
            EventType = EventType.OfflineMessage,
            MsgId = message.MsgId ?? "",
            UserId = message.UserId ?? "",
            DeviceId = message.DeviceId ?? "",
            ToUserId = message.ToUserId ?? "",
            GroupId = message.GroupId ?? "",
            EventData = ByteString.CopyFrom(message.ToByteArray()),
            Timestamp = unixTimestamp,
            NodeId = nodeId,
            TraceId = traceId ?? "",
        };

        SignIfPossible(evt, tenantSecret);
        return evt;
    }

    public static MqEvent CreateOfflineMessageForRecipient(
        MessageRequest message,
        string toUserId,
        string nodeId,
        string traceId,
        long unixTimestamp,
        string tenantSecret)
    {
        ArgumentNullException.ThrowIfNull(message);

        var evt = new MqEvent
        {
            TenantId = message.TenantId ?? "",
            EventType = EventType.OfflineMessage,
            MsgId = message.MsgId ?? "",
            UserId = message.UserId ?? "",
            DeviceId = message.DeviceId ?? "",
            ToUserId = toUserId ?? "",
            GroupId = message.GroupId ?? "",
            EventData = ByteString.CopyFrom(message.ToByteArray()),
            Timestamp = unixTimestamp,
            NodeId = nodeId,
            TraceId = traceId ?? "",
        };

        SignIfPossible(evt, tenantSecret);
        return evt;
    }

    private static void SignIfPossible(MqEvent evt, string tenantSecret)
    {
        if (string.IsNullOrWhiteSpace(tenantSecret))
        {
            evt.Sign = "";
            return;
        }

        var payload = evt.Clone();
        payload.Sign = "";
        evt.Sign = HmacSign.ComputeBase64(tenantSecret, payload);
    }
}
