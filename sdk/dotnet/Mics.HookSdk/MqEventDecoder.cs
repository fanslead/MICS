using Google.Protobuf;
using Mics.Contracts.Hook.V1;
using Mics.Contracts.Message.V1;

namespace Mics.HookSdk;

public static class MqEventDecoder
{
    public static bool TryDecodeConnectAck(MqEvent evt, out ConnectAck ack)
    {
        ack = new ConnectAck();
        if (evt is null || evt.EventData.IsEmpty)
        {
            return false;
        }

        if (evt.EventType is not (EventType.ConnectOnline or EventType.ConnectOffline))
        {
            return false;
        }

        try
        {
            ack = ConnectAck.Parser.ParseFrom(evt.EventData);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool TryDecodeMessage(MqEvent evt, out MessageRequest message)
    {
        message = new MessageRequest();
        if (evt is null || evt.EventData.IsEmpty)
        {
            return false;
        }

        if (evt.EventType is not (EventType.SingleChatMsg or EventType.GroupChatMsg))
        {
            return false;
        }

        try
        {
            message = MessageRequest.Parser.ParseFrom(evt.EventData);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool TryVerifyAndDecodeConnectAck(string tenantSecret, MqEvent evt, out ConnectAck ack)
    {
        ack = new ConnectAck();
        if (!MqEventVerifier.Verify(tenantSecret, evt))
        {
            return false;
        }

        return TryDecodeConnectAck(evt, out ack);
    }

    public static bool TryVerifyAndDecodeMessage(string tenantSecret, MqEvent evt, out MessageRequest message)
    {
        message = new MessageRequest();
        if (!MqEventVerifier.Verify(tenantSecret, evt))
        {
            return false;
        }

        return TryDecodeMessage(evt, out message);
    }
}

