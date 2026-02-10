import { describe, expect, test } from "vitest";
import { decodeAndVerifyMqEvent } from "./mq.js";
import { EventType, MqEventCodec } from "./proto/mics_hook.js";
import { computeMqEventSignBase64 } from "./signing.js";

describe("decodeAndVerifyMqEvent", () => {
  test("returns ok when signature is optional and missing", () => {
    const evt = {
      tenantId: "t1",
      eventType: EventType.CONNECT_ONLINE,
      msgId: "",
      userId: "u1",
      deviceId: "d1",
      toUserId: "",
      groupId: "",
      eventData: new Uint8Array([1]),
      timestamp: 123,
      nodeId: "node-1",
      sign: "",
      traceId: "tr1",
    };
    const bytes = MqEventCodec.encode(evt).finish();
    const res = decodeAndVerifyMqEvent(bytes, { tenantSecret: "secret", requireSign: false });
    expect(res.ok).toBe(true);
    expect(res.event.tenantId).toBe("t1");
  });

  test("rejects when signature is required but invalid", () => {
    const evt = {
      tenantId: "t1",
      eventType: EventType.SINGLE_CHAT_MSG,
      msgId: "m1",
      userId: "u1",
      deviceId: "d1",
      toUserId: "u2",
      groupId: "",
      eventData: new Uint8Array([1]),
      timestamp: 123,
      nodeId: "node-1",
      sign: "bad",
      traceId: "tr1",
    };
    const bytes = MqEventCodec.encode(evt).finish();
    const res = decodeAndVerifyMqEvent(bytes, { tenantSecret: "secret", requireSign: true });
    expect(res.ok).toBe(false);
    expect(res.reason).toContain("invalid sign");
  });

  test("accepts when signature is required and valid", () => {
    const evt = {
      tenantId: "t1",
      eventType: EventType.SINGLE_CHAT_MSG,
      msgId: "m1",
      userId: "u1",
      deviceId: "d1",
      toUserId: "u2",
      groupId: "",
      eventData: new Uint8Array([1]),
      timestamp: 123,
      nodeId: "node-1",
      sign: "",
      traceId: "tr1",
    };
    const sign = computeMqEventSignBase64("secret", { ...evt, sign: "ignored" });
    const bytes = MqEventCodec.encode({ ...evt, sign }).finish();
    const res = decodeAndVerifyMqEvent(bytes, { tenantSecret: "secret", requireSign: true });
    expect(res.ok).toBe(true);
    expect(res.event.msgId).toBe("m1");
  });
});

