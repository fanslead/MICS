package com.mics.clientsdk

import org.assertj.core.api.Assertions.assertThat
import org.junit.jupiter.api.Test
import java.util.Random

class AesGcmMessageCryptoTest {
    @Test
    fun `encrypt then decrypt should roundtrip`() {
        val key = ByteArray(32) { 0 }
        val crypto = AesGcmMessageCrypto(key)

        val plain = ByteArray(1024)
        Random(1).nextBytes(plain)

        val enc = crypto.encrypt(plain)
        assertThat(enc).isNotEqualTo(plain)

        val dec = crypto.decrypt(enc)
        assertThat(dec).isEqualTo(plain)
    }
}

