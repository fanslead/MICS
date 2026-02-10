package com.mics.hooksdk;

import com.mics.contracts.hook.v1.AuthRequest;
import com.mics.contracts.hook.v1.HookMeta;
import org.junit.jupiter.api.Test;

import static org.assertj.core.api.Assertions.assertThat;

public class HookSignerTest {
    @Test
    void verify_should_accept_valid_signature() {
        String secret = "secret";

        HookMeta metaNoSign = HookMeta.newBuilder()
                .setTenantId("t1")
                .setRequestId("rid-1")
                .setTimestampMs(123456789L)
                .setSign("")
                .setTraceId("tr-1")
                .build();

        AuthRequest payloadForSign = AuthRequest.newBuilder()
                .setMeta(metaNoSign)
                .setToken("valid:u1")
                .setDeviceId("dev1")
                .build();

        String sign = HookSigner.computeBase64(secret, metaNoSign, payloadForSign);
        HookMeta meta = metaNoSign.toBuilder().setSign(sign).build();

        AuthRequest signed = payloadForSign.toBuilder().setMeta(meta).build();
        AuthRequest payloadForVerify = signed.toBuilder()
                .setMeta(meta.toBuilder().clearSign().build())
                .build();

        assertThat(HookSigner.verify(secret, meta, payloadForVerify, true)).isTrue();
    }

    @Test
    void verify_should_reject_non_canonical_base64() {
        String secret = "secret";

        HookMeta metaNoSign = HookMeta.newBuilder()
                .setTenantId("t1")
                .setRequestId("rid-1")
                .setTimestampMs(123456789L)
                .setSign("")
                .build();

        AuthRequest payloadForSign = AuthRequest.newBuilder()
                .setMeta(metaNoSign)
                .setToken("valid:u1")
                .setDeviceId("dev1")
                .build();

        String sign = HookSigner.computeBase64(secret, metaNoSign, payloadForSign);
        String nonCanonical = sign.endsWith("=") ? sign.substring(0, sign.length() - 1) : sign;
        HookMeta meta = metaNoSign.toBuilder().setSign(nonCanonical).build();

        AuthRequest payloadForVerify = payloadForSign.toBuilder()
                .setMeta(meta.toBuilder().clearSign().build())
                .build();

        assertThat(HookSigner.verify(secret, meta, payloadForVerify, true)).isFalse();
    }
}

