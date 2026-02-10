import { AES } from "@stablelib/aes";
import { GCM } from "@stablelib/gcm";

import type { MessageCrypto } from "./types";

const VERSION = 1;
const NONCE_BYTES = 12;
const TAG_BYTES = 16;

function randomBytes(len: number): Uint8Array {
  const out = new Uint8Array(len);

  const wxAny = (globalThis as any).wx;
  if (wxAny?.getRandomValues) {
    try {
      wxAny.getRandomValues(out);
      return out;
    } catch {
      // fallthrough
    }
  }

  const cryptoAny = (globalThis as any).crypto;
  if (cryptoAny?.getRandomValues) {
    try {
      cryptoAny.getRandomValues(out);
      return out;
    } catch {
      // fallthrough
    }
  }

  for (let i = 0; i < len; i++) {
    out[i] = (Math.random() * 256) | 0;
  }
  return out;
}

export class AesGcmMessageCrypto implements MessageCrypto {
  private readonly aead: GCM;

  constructor(rawKey: Uint8Array) {
    if (rawKey.byteLength !== 16 && rawKey.byteLength !== 24 && rawKey.byteLength !== 32) {
      throw new Error("AES key length must be 16/24/32 bytes");
    }
    this.aead = new GCM(new AES(rawKey, true));
  }

  async encrypt(plaintext: Uint8Array): Promise<Uint8Array> {
    if (plaintext.byteLength === 0) return new Uint8Array();
    const nonce = randomBytes(NONCE_BYTES);
    const sealed = this.aead.seal(nonce, plaintext);
    const ciphertext = sealed.subarray(0, sealed.byteLength - TAG_BYTES);
    const tag = sealed.subarray(sealed.byteLength - TAG_BYTES);

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

    const nonce = ciphertext.subarray(1, 1 + NONCE_BYTES);
    const tag = ciphertext.subarray(1 + NONCE_BYTES, 1 + NONCE_BYTES + TAG_BYTES);
    const enc = ciphertext.subarray(1 + NONCE_BYTES + TAG_BYTES);

    const combined = new Uint8Array(enc.byteLength + TAG_BYTES);
    combined.set(enc, 0);
    combined.set(tag, enc.byteLength);

    const plain = this.aead.open(nonce, combined);
    if (!plain) throw new Error("decrypt failed");
    return plain;
  }
}
