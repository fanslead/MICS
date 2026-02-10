package com.mics.hooksdk;

import com.google.protobuf.Message;
import com.mics.contracts.hook.v1.HookMeta;

import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.util.Base64;

public final class HookSigner {
    private HookSigner() {
    }

    public static String computeBase64(String tenantSecret, HookMeta meta, Message payloadWithMetaSignCleared) {
        if (tenantSecret == null || tenantSecret.isBlank()) {
            throw new IllegalArgumentException("tenantSecret is blank");
        }
        return computeBase64(tenantSecret.getBytes(StandardCharsets.UTF_8), meta, payloadWithMetaSignCleared);
    }

    public static String computeBase64(byte[] tenantSecret, HookMeta meta, Message payloadWithMetaSignCleared) {
        if (meta == null) {
            throw new IllegalArgumentException("meta is null");
        }
        if (payloadWithMetaSignCleared == null) {
            throw new IllegalArgumentException("payloadWithMetaSignCleared is null");
        }

        byte[] payloadBytes = payloadWithMetaSignCleared.toByteArray();
        byte[] requestIdBytes = meta.getRequestId().getBytes(StandardCharsets.UTF_8);
        byte[] timestampLe64 = ByteBuffer.allocate(Long.BYTES).order(ByteOrder.LITTLE_ENDIAN).putLong(meta.getTimestampMs()).array();
        byte[] sig = HmacSign.hmacSha256(tenantSecret, payloadBytes, requestIdBytes, timestampLe64);
        return Base64.getEncoder().encodeToString(sig);
    }

    public static boolean verify(String tenantSecret, HookMeta meta, Message payloadWithMetaSignCleared, boolean requireSign) {
        if (tenantSecret == null || tenantSecret.isBlank()) {
            return false;
        }
        return verify(tenantSecret.getBytes(StandardCharsets.UTF_8), meta, payloadWithMetaSignCleared, requireSign);
    }

    public static boolean verify(byte[] tenantSecret, HookMeta meta, Message payloadWithMetaSignCleared, boolean requireSign) {
        if (meta == null) {
            return false;
        }
        String sign = meta.getSign();
        if (requireSign && (sign == null || sign.isBlank())) {
            return false;
        }
        if (sign == null || sign.isBlank()) {
            return true;
        }

        byte[] expected = Base64.getDecoder().decode(computeBase64(tenantSecret, meta, payloadWithMetaSignCleared));
        byte[] provided;
        try {
            provided = CanonicalBase64.decodeCanonical(sign);
        } catch (IllegalArgumentException e) {
            return false;
        }
        return MessageDigest.isEqual(expected, provided);
    }
}

