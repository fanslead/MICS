import type { MessageCrypto } from "./types";

const VERSION = 1;
const NONCE_BYTES = 12;
const TAG_BYTES = 16;

export class AesGcmMessageCrypto implements MessageCrypto {
  private readonly keyPromise: Promise<CryptoKey>;

  constructor(rawKey: Uint8Array) {
    if (rawKey.byteLength !== 16 && rawKey.byteLength !== 24 && rawKey.byteLength !== 32) {
      throw new Error("AES key length must be 16/24/32 bytes");
    }
    this.keyPromise = crypto.subtle.importKey(
      "raw",
      rawKey as unknown as BufferSource,
      { name: "AES-GCM" },
      false,
      ["encrypt", "decrypt"]
    );
  }

  async encrypt(plaintext: Uint8Array): Promise<Uint8Array> {
    if (plaintext.byteLength === 0) return new Uint8Array();
    const key = await this.keyPromise;
    const nonce = crypto.getRandomValues(new Uint8Array(NONCE_BYTES));
    const encrypted = new Uint8Array(
      await crypto.subtle.encrypt(
        { name: "AES-GCM", iv: nonce as unknown as BufferSource, tagLength: TAG_BYTES * 8 },
        key,
        plaintext as unknown as BufferSource
      )
    );

    const ciphertext = encrypted.subarray(0, encrypted.byteLength - TAG_BYTES);
    const tag = encrypted.subarray(encrypted.byteLength - TAG_BYTES);

    const out = new Uint8Array(1 + NONCE_BYTES + TAG_BYTES + ciphertext.byteLength);
    out[0] = VERSION;
    out.set(nonce, 1);
    out.set(tag, 1 + NONCE_BYTES);
    out.set(ciphertext, 1 + NONCE_BYTES + TAG_BYTES);
    return out;
  }

  async decrypt(ciphertext: Uint8Array): Promise<Uint8Array> {
    if (ciphertext.byteLength === 0) return new Uint8Array();
    if (ciphertext.byteLength < 1 + NONCE_BYTES + TAG_BYTES) throw new Error("ciphertext too short");
    if (ciphertext[0] !== VERSION) throw new Error("unsupported ciphertext version");

    const key = await this.keyPromise;
    const nonce = ciphertext.subarray(1, 1 + NONCE_BYTES);
    const tag = ciphertext.subarray(1 + NONCE_BYTES, 1 + NONCE_BYTES + TAG_BYTES);
    const enc = ciphertext.subarray(1 + NONCE_BYTES + TAG_BYTES);

    const combined = new Uint8Array(enc.byteLength + TAG_BYTES);
    combined.set(enc, 0);
    combined.set(tag, enc.byteLength);

    return new Uint8Array(
      await crypto.subtle.decrypt(
        { name: "AES-GCM", iv: nonce as unknown as BufferSource, tagLength: TAG_BYTES * 8 },
        key,
        combined as unknown as BufferSource
      )
    );
  }
}
