package com.mics.clientsdk

import java.security.SecureRandom
import javax.crypto.Cipher
import javax.crypto.spec.GCMParameterSpec
import javax.crypto.spec.SecretKeySpec

class AesGcmMessageCrypto(rawKey: ByteArray) : MessageCrypto {
    init {
        require(rawKey.size == 16 || rawKey.size == 24 || rawKey.size == 32) {
            "AES key length must be 16/24/32 bytes"
        }
    }

    private val key = SecretKeySpec(rawKey, "AES")
    private val rnd = SecureRandom()

    override fun encrypt(plaintext: ByteArray): ByteArray {
        if (plaintext.isEmpty()) return ByteArray(0)

        val nonce = ByteArray(NONCE_BYTES)
        rnd.nextBytes(nonce)

        val cipher = Cipher.getInstance(TRANSFORM)
        cipher.init(Cipher.ENCRYPT_MODE, key, GCMParameterSpec(TAG_BITS, nonce))
        val sealed = cipher.doFinal(plaintext)

        val ciphertextLen = sealed.size - TAG_BYTES
        val ciphertext = sealed.copyOfRange(0, ciphertextLen)
        val tag = sealed.copyOfRange(ciphertextLen, sealed.size)

        val out = ByteArray(1 + NONCE_BYTES + TAG_BYTES + ciphertext.size)
        out[0] = VERSION
        nonce.copyInto(out, destinationOffset = 1)
        tag.copyInto(out, destinationOffset = 1 + NONCE_BYTES)
        ciphertext.copyInto(out, destinationOffset = 1 + NONCE_BYTES + TAG_BYTES)
        return out
    }

    override fun decrypt(ciphertext: ByteArray): ByteArray {
        if (ciphertext.isEmpty()) return ByteArray(0)
        if (ciphertext.size < 1 + NONCE_BYTES + TAG_BYTES) error("ciphertext too short")
        if (ciphertext[0] != VERSION) error("unsupported ciphertext version")

        val nonce = ciphertext.copyOfRange(1, 1 + NONCE_BYTES)
        val tag = ciphertext.copyOfRange(1 + NONCE_BYTES, 1 + NONCE_BYTES + TAG_BYTES)
        val enc = ciphertext.copyOfRange(1 + NONCE_BYTES + TAG_BYTES, ciphertext.size)

        val combined = ByteArray(enc.size + TAG_BYTES)
        enc.copyInto(combined, 0)
        tag.copyInto(combined, enc.size)

        val cipher = Cipher.getInstance(TRANSFORM)
        cipher.init(Cipher.DECRYPT_MODE, key, GCMParameterSpec(TAG_BITS, nonce))
        return cipher.doFinal(combined)
    }

    companion object {
        private const val VERSION: Byte = 1
        private const val NONCE_BYTES = 12
        private const val TAG_BYTES = 16
        private const val TAG_BITS = TAG_BYTES * 8
        private const val TRANSFORM = "AES/GCM/NoPadding"
    }
}
