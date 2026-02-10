package com.mics.hooksdk;

import com.google.protobuf.ByteString;
import com.mics.contracts.hook.v1.EventType;
import com.mics.contracts.hook.v1.MqEvent;
import com.mics.contracts.message.v1.ConnectAck;
import com.mics.contracts.message.v1.MessageRequest;
import com.mics.contracts.message.v1.MessageType;
import org.junit.jupiter.api.Test;

import static org.assertj.core.api.Assertions.assertThat;

public class MqEventSignerDecoderTest {
    @Test
    void mq_sign_verify_and_decode_message() {
        String secret = "secret";

        MessageRequest msg = MessageRequest.newBuilder()
                .setTenantId("t1")
                .setUserId("u1")
                .setDeviceId("d1")
                .setMsgId("m1")
                .setMsgType(MessageType.SINGLE_CHAT)
                .setToUserId("u2")
                .setMsgBody(ByteString.copyFromUtf8("hi"))
                .setTimestampMs(1L)
                .build();

        MqEvent evtNoSign = MqEvent.newBuilder()
                .setTenantId("t1")
                .setEventType(EventType.SINGLE_CHAT_MSG)
                .setMsgId("m1")
                .setUserId("u1")
                .setDeviceId("d1")
                .setToUserId("u2")
                .setGroupId("")
                .setEventData(msg.toByteString())
                .setTimestamp(2L)
                .setNodeId("node-1")
                .setTraceId("tr")
                .setSign("")
                .build();

        String sign = MqEventSigner.computeBase64(secret, evtNoSign);
        MqEvent evt = evtNoSign.toBuilder().setSign(sign).build();

        assertThat(MqEventSigner.verify(secret, evt, true)).isTrue();
        assertThat(MqEventDecoder.tryDecodeMessage(evt).isPresent()).isTrue();
        assertThat(MqEventDecoder.tryDecodeMessage(evt).get().getMsgId()).isEqualTo("m1");
    }

    @Test
    void mq_decode_connect_ack() {
        ConnectAck ack = ConnectAck.newBuilder()
                .setCode(1000)
                .setTenantId("t1")
                .setUserId("u1")
                .setDeviceId("d1")
                .setNodeId("node-1")
                .setTraceId("tr")
                .build();

        MqEvent evt = MqEvent.newBuilder()
                .setTenantId("t1")
                .setEventType(EventType.CONNECT_ONLINE)
                .setUserId("u1")
                .setDeviceId("d1")
                .setEventData(ack.toByteString())
                .setTimestamp(2L)
                .setNodeId("node-1")
                .build();

        assertThat(MqEventDecoder.tryDecodeConnectAck(evt).isPresent()).isTrue();
        assertThat(MqEventDecoder.tryDecodeConnectAck(evt).get().getUserId()).isEqualTo("u1");
    }
}

