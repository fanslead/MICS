import { describe, expect, test } from "vitest";
import { dlqTopicName, eventTopicName, decodeMqEvent, isValidMqEventSign } from "./mq.js";
import { EventType, MqEventCodec } from "./proto/mics_hook.js";
import { computeMqEventSignBase64 } from "./signing.js";

describe("mq", () => {
  test("topic naming is per-tenant", () => {
    expect(eventTopicName("t1")).toBe("im-mics-t1-event");
    expect(dlqTopicName("t1")).toBe("im-mics-t1-event-dlq");
  });

  test("decodeMqEvent decodes protobuf bytes", () => {
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
    const decoded = decodeMqEvent(bytes);
    expect(decoded.tenantId).toBe("t1");
    expect(decoded.userId).toBe("u1");
  });

  test("isValidMqEventSign verifies sign", () => {
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
    expect(isValidMqEventSign("secret", { ...evt, sign })).toBe(true);
    expect(isValidMqEventSign("secret", { ...evt, sign: sign + "x" })).toBe(false);
  });
});
