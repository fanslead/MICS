package com.mics.hooksdk.http;

import com.google.protobuf.ByteString;
import com.mics.contracts.hook.v1.CheckMessageRequest;
import com.mics.contracts.hook.v1.CheckMessageResponse;
import com.mics.contracts.hook.v1.HookMeta;
import com.mics.contracts.message.v1.MessageRequest;
import com.mics.contracts.message.v1.MessageType;
import com.mics.hooksdk.HookSigner;
import org.junit.jupiter.api.Test;

import java.net.InetSocketAddress;
import java.net.URI;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.time.Duration;
import java.util.Map;

import static org.assertj.core.api.Assertions.assertThat;

public class MicsHookHttpServerTest {
    @Test
    void check_message_roundtrip_should_verify_and_echo_meta() throws Exception {
        String secret = "secret";

        MicsHookHandler handler = new MicsHookHandler() {
            @Override
            public com.mics.contracts.hook.v1.AuthResponse onAuth(com.mics.contracts.hook.v1.AuthRequest request) {
                throw new UnsupportedOperationException();
            }

            @Override
            public CheckMessageResponse onCheckMessage(CheckMessageRequest request) {
                return CheckMessageResponse.newBuilder().setAllow(true).build();
            }

            @Override
            public com.mics.contracts.hook.v1.GetGroupMembersResponse onGetGroupMembers(com.mics.contracts.hook.v1.GetGroupMembersRequest request) {
                throw new UnsupportedOperationException();
            }
        };

        MicsHookServerOptions options = new MicsHookServerOptions(tid -> Map.of("t1", secret).get(tid), true);
        try (MicsHookHttpServer server = new MicsHookHttpServer(new InetSocketAddress("127.0.0.1", 0), handler, options)) {
            server.start();
            int port = server.getAddress().getPort();

            HookMeta metaNoSign = HookMeta.newBuilder()
                    .setTenantId("t1")
                    .setRequestId("rid")
                    .setTimestampMs(1L)
                    .setSign("")
                    .setTraceId("tr")
                    .build();

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

            CheckMessageRequest payloadForSign = CheckMessageRequest.newBuilder()
                    .setMeta(metaNoSign)
                    .setMessage(msg)
                    .build();

            String sign = HookSigner.computeBase64(secret, metaNoSign, payloadForSign);
            HookMeta meta = metaNoSign.toBuilder().setSign(sign).build();
            CheckMessageRequest req = payloadForSign.toBuilder().setMeta(meta).build();

            HttpClient client = HttpClient.newHttpClient();
            HttpRequest httpReq = HttpRequest.newBuilder()
                    .uri(URI.create("http://127.0.0.1:" + port + "/check-message"))
                    .timeout(Duration.ofSeconds(2))
                    .header("Content-Type", "application/protobuf")
                    .POST(HttpRequest.BodyPublishers.ofByteArray(req.toByteArray()))
                    .build();

            HttpResponse<byte[]> resp = client.send(httpReq, HttpResponse.BodyHandlers.ofByteArray());
            assertThat(resp.statusCode()).isEqualTo(200);

            CheckMessageResponse pb = CheckMessageResponse.parseFrom(resp.body());
            assertThat(pb.getAllow()).isTrue();
            assertThat(pb.getMeta().getTenantId()).isEqualTo("t1");
            assertThat(pb.getMeta().getTraceId()).isEqualTo("tr");
        }
    }
}

