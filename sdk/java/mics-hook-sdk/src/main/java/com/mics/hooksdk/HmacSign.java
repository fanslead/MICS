package com.mics.hooksdk;

import javax.crypto.Mac;
import javax.crypto.spec.SecretKeySpec;
import java.security.GeneralSecurityException;

final class HmacSign {
    private HmacSign() {
    }

    static byte[] hmacSha256(byte[] key, byte[]... parts) {
        if (key == null || key.length == 0) {
            throw new IllegalArgumentException("key is empty");
        }
        try {
            Mac mac = Mac.getInstance("HmacSHA256");
            mac.init(new SecretKeySpec(key, "HmacSHA256"));
            for (byte[] part : parts) {
                if (part != null && part.length > 0) {
                    mac.update(part);
                }
            }
            return mac.doFinal();
        } catch (GeneralSecurityException e) {
            throw new IllegalStateException("HmacSHA256 unavailable", e);
        }
    }
}

