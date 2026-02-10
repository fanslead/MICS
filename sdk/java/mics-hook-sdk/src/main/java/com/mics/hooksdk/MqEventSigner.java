package com.mics.hooksdk;

import com.mics.contracts.hook.v1.MqEvent;

import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.util.Base64;

public final class MqEventSigner {
    private MqEventSigner() {
    }

    public static String computeBase64(String tenantSecret, MqEvent evtWithSignCleared) {
        if (tenantSecret == null || tenantSecret.isBlank()) {
            throw new IllegalArgumentException("tenantSecret is blank");
        }
        return computeBase64(tenantSecret.getBytes(StandardCharsets.UTF_8), evtWithSignCleared);
    }

    public static String computeBase64(byte[] tenantSecret, MqEvent evtWithSignCleared) {
        if (evtWithSignCleared == null) {
            throw new IllegalArgumentException("evtWithSignCleared is null");
        }
        byte[] payload = evtWithSignCleared.toByteArray();
        byte[] sig = HmacSign.hmacSha256(tenantSecret, payload);
        return Base64.getEncoder().encodeToString(sig);
    }

    public static boolean verify(String tenantSecret, MqEvent evt, boolean requireSign) {
        if (tenantSecret == null || tenantSecret.isBlank()) {
            return false;
        }
        return verify(tenantSecret.getBytes(StandardCharsets.UTF_8), evt, requireSign);
    }

    public static boolean verify(byte[] tenantSecret, MqEvent evt, boolean requireSign) {
        if (evt == null) {
            return false;
        }
        String sign = evt.getSign();
        if (requireSign && (sign == null || sign.isBlank())) {
            return false;
        }
        if (sign == null || sign.isBlank()) {
            return true;
        }

        byte[] provided;
        try {
            provided = CanonicalBase64.decodeCanonical(sign);
        } catch (IllegalArgumentException e) {
            return false;
        }

        MqEvent payload = evt.toBuilder().clearSign().build();
        byte[] expected = Base64.getDecoder().decode(computeBase64(tenantSecret, payload));
        return MessageDigest.isEqual(expected, provided);
    }
}

