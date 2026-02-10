import { describe, expect, test } from "vitest";
import crypto from "node:crypto";
import { computeHookSignBase64, verifyHookSign, computeMqEventSignBase64, verifyMqEventSign } from "./signing.js";
import { AuthRequestCodec, EventType, MqEventCodec } from "./proto/mics_hook.js";

describe("signing", () => {
  test("computeHookSignBase64 matches HMAC(payload+requestId+timestampLE64)", () => {
    const meta = { tenantId: "t1", requestId: "r1", timestampMs: 123, sign: "", traceId: "tr1" };
    const req = { meta, token: "valid:u1", deviceId: "d1" };

    const payloadBytes = AuthRequestCodec.encode({ ...req, meta: { ...meta, sign: "" } }).finish();
    const requestIdBytes = Buffer.from(meta.requestId, "utf8");
    const tsBytes = Buffer.alloc(8);
    tsBytes.writeBigInt64LE(BigInt(meta.timestampMs), 0);

    const expected = crypto
      .createHmac("sha256", Buffer.from("secret", "utf8"))
      .update(payloadBytes)
      .update(requestIdBytes)
      .update(tsBytes)
      .digest("base64");

    const actual = computeHookSignBase64("secret", meta, payloadBytes);
    expect(actual).toBe(expected);

    expect(verifyHookSign("secret", meta, payloadBytes, actual)).toBe(true);
    expect(verifyHookSign("secret", meta, payloadBytes, actual + "x")).toBe(false);
  });

  test("mq event signing clears sign before hashing", () => {
    const evt = {
      tenantId: "t1",
      eventType: EventType.SINGLE_CHAT_MSG,
      msgId: "m1",
      userId: "u1",
      deviceId: "d1",
      toUserId: "u2",
      groupId: "",
      eventData: new Uint8Array([1, 2, 3]),
      timestamp: 123,
      nodeId: "node-1",
      sign: "will-be-cleared",
      traceId: "tr1",
    };

    const cleared = { ...evt, sign: "" };
    const bytes = MqEventCodec.encode(cleared).finish();
    const expected = crypto.createHmac("sha256", Buffer.from("secret", "utf8")).update(bytes).digest("base64");

    const actual = computeMqEventSignBase64("secret", evt);
    expect(actual).toBe(expected);
    expect(verifyMqEventSign("secret", evt, actual)).toBe(true);
  });
});
