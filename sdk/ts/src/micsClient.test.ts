import { describe, expect, it } from "vitest";
import { MicsClient } from "./micsClient";
import { MicsClientOptions } from "./options";
import { AckStatus, ClientFrameCodec, MessageType, ServerFrameCodec } from "./proto/mics_message";

class FakeWebSocket {
  static readonly CONNECTING = 0;
  static readonly OPEN = 1;
  static readonly CLOSING = 2;
  static readonly CLOSED = 3;

  binaryType: BinaryType = "arraybuffer";
  readyState = FakeWebSocket.CONNECTING;

  onopen: ((ev: any) => void) | null = null;
  onmessage: ((ev: any) => void) | null = null;
  onclose: ((ev: any) => void) | null = null;
  onerror: ((ev: any) => void) | null = null;

  sent: Uint8Array[] = [];

  open() {
    this.readyState = FakeWebSocket.OPEN;
    this.onopen?.({});
  }

  close(code?: number, reason?: string) {
    this.readyState = FakeWebSocket.CLOSED;
    this.onclose?.({ code: code ?? 1000, reason: reason ?? "" });
  }

  send(data: ArrayBuffer | ArrayBufferView) {
    const bytes =
      data instanceof ArrayBuffer
        ? new Uint8Array(data)
        : new Uint8Array(data.buffer, data.byteOffset, data.byteLength);
    this.sent.push(bytes);
  }

  pushServerFrame(frame: any) {
    const bytes = ServerFrameCodec.encode(frame).finish();
    this.onmessage?.({ data: bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength) });
  }
}

describe("MicsClient (TS)", () => {
  it("connects and exposes session from ConnectAck", async () => {
    const ws = new FakeWebSocket();

    const client = new MicsClient(
      MicsClientOptions.default({ heartbeatIntervalMs: 0, autoReconnect: false }),
      { wsFactory: () => ws as any }
    );

    const seen: any[] = [];
    client.onConnected = (s) => seen.push(s);

    const p = client.connect({ url: "ws://localhost/ws", tenantId: "t1", token: "valid:u1", deviceId: "d1" });
    ws.open();
    ws.pushServerFrame({
      payload: "connectAck",
      connectAck: { code: 1000, tenantId: "t1", userId: "u1", deviceId: "d1", nodeId: "n1", traceId: "tr1" }
    });

    const session = await p;
    expect(session.tenantId).toBe("t1");
    expect(session.userId).toBe("u1");
    expect(seen.length).toBe(1);
  });

  it("sends message frame and resolves ack", async () => {
    const ws = new FakeWebSocket();
    const client = new MicsClient(MicsClientOptions.default({ heartbeatIntervalMs: 0, autoReconnect: false }), {
      wsFactory: () => ws as any
    });

    const connectP = client.connect({ url: "ws://localhost/ws", tenantId: "t1", token: "valid:u1", deviceId: "d1" });
    ws.open();
    ws.pushServerFrame({
      payload: "connectAck",
      connectAck: { code: 1000, tenantId: "t1", userId: "u1", deviceId: "d1", nodeId: "n1", traceId: "tr1" }
    });
    await connectP;

    const ackP = client.sendSingleChat({ toUserId: "u2", msgBody: new Uint8Array([1, 2, 3]), msgId: "m1" });

    await new Promise((r) => setTimeout(r, 0));
    expect(ws.sent.length).toBe(1);
    const frame = ClientFrameCodec.decode(ws.sent[0]);
    expect(frame.payload).toBe("message");
    if (frame.payload === "message") {
      expect(frame.message.msgId).toBe("m1");
      expect(frame.message.msgType).toBe(MessageType.SINGLE_CHAT);
      expect(frame.message.toUserId).toBe("u2");
    }

    ws.pushServerFrame({ payload: "ack", ack: { msgId: "m1", status: AckStatus.SENT, timestampMs: 1, reason: "" } });
    const ack = await ackP;
    expect(ack.status).toBe(AckStatus.SENT);
  });

  it("does not retry when server returns FAILED ack", async () => {
    const ws = new FakeWebSocket();
    const client = new MicsClient(
      MicsClientOptions.default({ heartbeatIntervalMs: 0, autoReconnect: false, ackTimeoutMs: 1000, maxSendAttempts: 3 }),
      { wsFactory: () => ws as any }
    );

    const connectP = client.connect({ url: "ws://localhost/ws", tenantId: "t1", token: "valid:u1", deviceId: "d1" });
    ws.open();
    ws.pushServerFrame({
      payload: "connectAck",
      connectAck: { code: 1000, tenantId: "t1", userId: "u1", deviceId: "d1", nodeId: "n1", traceId: "tr1" }
    });
    await connectP;

    const ackP = client.sendSingleChat({ toUserId: "u2", msgBody: new Uint8Array([1, 2, 3]), msgId: "m1" });
    await new Promise((r) => setTimeout(r, 0));
    expect(ws.sent.length).toBe(1);

    ws.pushServerFrame({ payload: "ack", ack: { msgId: "m1", status: AckStatus.FAILED, timestampMs: 1, reason: "blocked" } });
    const ack = await ackP;
    expect(ack.status).toBe(AckStatus.FAILED);
    expect(ws.sent.length).toBe(1);
  });
});
