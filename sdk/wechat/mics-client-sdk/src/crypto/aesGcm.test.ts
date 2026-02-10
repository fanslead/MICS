import { describe, expect, it } from "vitest";

import { AesGcmMessageCrypto } from "./aesGcm";

describe("AesGcmMessageCrypto", () => {
  it("encrypt/decrypt roundtrip", async () => {
    const crypto = new AesGcmMessageCrypto(new Uint8Array(32));
    const plain = new Uint8Array(Array.from({ length: 512 }, (_, i) => i & 0xff));
    const enc = await crypto.encrypt(plain);
    expect(enc).not.toEqual(plain);
    const dec = await crypto.decrypt(enc);
    expect(dec).toEqual(plain);
  });
});

