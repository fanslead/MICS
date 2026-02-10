package com.mics.clientsdk

import com.mics.contracts.message.v1.ClientFrame
import com.mics.contracts.message.v1.MessageRequest
import com.mics.contracts.message.v1.MessageType
import com.mics.contracts.message.v1.ServerFrame
import org.assertj.core.api.Assertions.assertThat
import org.junit.jupiter.api.Test

class ProtoSmokeTest {
    @Test
    fun `client frame encode decode`() {
        val msg = MessageRequest.newBuilder()
            .setTenantId("t1")
            .setUserId("u1")
            .setDeviceId("d1")
            .setMsgId("m1")
            .setMsgType(MessageType.SINGLE_CHAT)
            .setToUserId("u2")
            .setTimestampMs(1)
            .build()

        val frame = ClientFrame.newBuilder().setMessage(msg).build()
        val bytes = frame.toByteArray()
        val parsed = ClientFrame.parseFrom(bytes)
        assertThat(parsed.message.msgId).isEqualTo("m1")
    }

    @Test
    fun `server frame payload case`() {
        val bytes = ServerFrame.getDefaultInstance().toByteArray()
        val parsed = ServerFrame.parseFrom(bytes)
        assertThat(parsed.payloadCase).isEqualTo(ServerFrame.PayloadCase.PAYLOAD_NOT_SET)
    }
}

