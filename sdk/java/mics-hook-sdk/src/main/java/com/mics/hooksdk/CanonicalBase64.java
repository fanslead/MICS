package com.mics.hooksdk;

import java.util.Base64;

final class CanonicalBase64 {
    private CanonicalBase64() {
    }

    static byte[] decodeCanonical(String base64) {
        if (base64 == null || base64.isBlank()) {
            throw new IllegalArgumentException("base64 is blank");
        }

        byte[] decoded = Base64.getDecoder().decode(base64);
        String encoded = Base64.getEncoder().encodeToString(decoded);
        if (!encoded.equals(base64)) {
            throw new IllegalArgumentException("base64 is not canonical");
        }
        return decoded;
    }
}

