package com.mics.hooksdk;

import com.google.protobuf.InvalidProtocolBufferException;
import com.mics.contracts.hook.v1.EventType;
import com.mics.contracts.hook.v1.MqEvent;
import com.mics.contracts.message.v1.ConnectAck;
import com.mics.contracts.message.v1.MessageRequest;

import java.util.Optional;

public final class MqEventDecoder {
    private MqEventDecoder() {
    }

    public static Optional<ConnectAck> tryDecodeConnectAck(MqEvent evt) {
        if (evt == null || evt.getEventData().isEmpty()) {
            return Optional.empty();
        }
        if (evt.getEventType() != EventType.CONNECT_ONLINE && evt.getEventType() != EventType.CONNECT_OFFLINE) {
            return Optional.empty();
        }
        try {
            return Optional.of(ConnectAck.parseFrom(evt.getEventData()));
        } catch (InvalidProtocolBufferException e) {
            return Optional.empty();
        }
    }

    public static Optional<MessageRequest> tryDecodeMessage(MqEvent evt) {
        if (evt == null || evt.getEventData().isEmpty()) {
            return Optional.empty();
        }
        if (evt.getEventType() != EventType.SINGLE_CHAT_MSG && evt.getEventType() != EventType.GROUP_CHAT_MSG) {
            return Optional.empty();
        }
        try {
            return Optional.of(MessageRequest.parseFrom(evt.getEventData()));
        } catch (InvalidProtocolBufferException e) {
            return Optional.empty();
        }
    }

    public static Optional<ConnectAck> tryVerifyAndDecodeConnectAck(String tenantSecret, MqEvent evt, boolean requireSign) {
        if (!MqEventSigner.verify(tenantSecret, evt, requireSign)) {
            return Optional.empty();
        }
        return tryDecodeConnectAck(evt);
    }

    public static Optional<MessageRequest> tryVerifyAndDecodeMessage(String tenantSecret, MqEvent evt, boolean requireSign) {
        if (!MqEventSigner.verify(tenantSecret, evt, requireSign)) {
            return Optional.empty();
        }
        return tryDecodeMessage(evt);
    }
}

