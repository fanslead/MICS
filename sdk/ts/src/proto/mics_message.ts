import _m0 from "protobufjs/minimal";

export enum MessageType {
  SINGLE_CHAT = 0,
  GROUP_CHAT = 1,
  UNRECOGNIZED = -1
}

export enum AckStatus {
  SENT = 0,
  FAILED = 1,
  UNRECOGNIZED = -1
}

export interface MessageRequest {
  tenantId: string;
  userId: string;
  deviceId: string;
  msgId: string;
  msgType: MessageType;
  toUserId: string;
  groupId: string;
  msgBody: Uint8Array;
  timestampMs: number;
}

export interface MessageAck {
  msgId: string;
  status: AckStatus;
  timestampMs: number;
  reason: string;
}

export interface ConnectAck {
  code: number;
  tenantId: string;
  userId: string;
  deviceId: string;
  nodeId: string;
  traceId: string;
}

export interface MessageDelivery {
  message?: MessageRequest;
}

export interface ServerError {
  code: number;
  message: string;
}

export interface HeartbeatPing {
  timestampMs: number;
}

export interface HeartbeatPong {
  timestampMs: number;
}

export type ClientFrame =
  | { payload: "message"; message: MessageRequest }
  | { payload: "heartbeatPing"; heartbeatPing: HeartbeatPing }
  | { payload: undefined };

export type ServerFrame =
  | { payload: "connectAck"; connectAck: ConnectAck }
  | { payload: "delivery"; delivery: MessageDelivery }
  | { payload: "ack"; ack: MessageAck }
  | { payload: "error"; error: ServerError }
  | { payload: "heartbeatPong"; heartbeatPong: HeartbeatPong }
  | { payload: undefined };

function createBaseMessageRequest(): MessageRequest {
  return {
    tenantId: "",
    userId: "",
    deviceId: "",
    msgId: "",
    msgType: 0,
    toUserId: "",
    groupId: "",
    msgBody: new Uint8Array(),
    timestampMs: 0
  };
}

export const MessageRequestCodec = {
  encode(message: MessageRequest, writer: _m0.Writer = _m0.Writer.create()): _m0.Writer {
    if (message.tenantId !== "") writer.uint32(10).string(message.tenantId);
    if (message.userId !== "") writer.uint32(18).string(message.userId);
    if (message.deviceId !== "") writer.uint32(26).string(message.deviceId);
    if (message.msgId !== "") writer.uint32(34).string(message.msgId);
    if (message.msgType !== 0) writer.uint32(40).int32(message.msgType);
    if (message.toUserId !== "") writer.uint32(50).string(message.toUserId);
    if (message.groupId !== "") writer.uint32(58).string(message.groupId);
    if (message.msgBody.length !== 0) writer.uint32(66).bytes(message.msgBody);
    if (message.timestampMs !== 0) writer.uint32(72).int64(message.timestampMs);
    return writer;
  },
  decode(input: _m0.Reader | Uint8Array, length?: number): MessageRequest {
    const reader = input instanceof _m0.Reader ? input : new _m0.Reader(input);
    const end = length === undefined ? reader.len : reader.pos + length;
    const message = createBaseMessageRequest();
    while (reader.pos < end) {
      const tag = reader.uint32();
      switch (tag >>> 3) {
        case 1:
          message.tenantId = reader.string();
          break;
        case 2:
          message.userId = reader.string();
          break;
        case 3:
          message.deviceId = reader.string();
          break;
        case 4:
          message.msgId = reader.string();
          break;
        case 5:
          message.msgType = reader.int32() as MessageType;
          break;
        case 6:
          message.toUserId = reader.string();
          break;
        case 7:
          message.groupId = reader.string();
          break;
        case 8:
          message.msgBody = reader.bytes();
          break;
        case 9:
          message.timestampMs = longToNumber(reader.int64() as any);
          break;
        default:
          reader.skipType(tag & 7);
          break;
      }
    }
    return message;
  }
};

function createBaseMessageAck(): MessageAck {
  return { msgId: "", status: 0, timestampMs: 0, reason: "" };
}

export const MessageAckCodec = {
  encode(message: MessageAck, writer: _m0.Writer = _m0.Writer.create()): _m0.Writer {
    if (message.msgId !== "") writer.uint32(10).string(message.msgId);
    if (message.status !== 0) writer.uint32(16).int32(message.status);
    if (message.timestampMs !== 0) writer.uint32(24).int64(message.timestampMs);
    if (message.reason !== "") writer.uint32(34).string(message.reason);
    return writer;
  },
  decode(input: _m0.Reader | Uint8Array, length?: number): MessageAck {
    const reader = input instanceof _m0.Reader ? input : new _m0.Reader(input);
    const end = length === undefined ? reader.len : reader.pos + length;
    const message = createBaseMessageAck();
    while (reader.pos < end) {
      const tag = reader.uint32();
      switch (tag >>> 3) {
        case 1:
          message.msgId = reader.string();
          break;
        case 2:
          message.status = reader.int32() as AckStatus;
          break;
        case 3:
          message.timestampMs = longToNumber(reader.int64() as any);
          break;
        case 4:
          message.reason = reader.string();
          break;
        default:
          reader.skipType(tag & 7);
          break;
      }
    }
    return message;
  }
};

function createBaseConnectAck(): ConnectAck {
  return { code: 0, tenantId: "", userId: "", deviceId: "", nodeId: "", traceId: "" };
}

export const ConnectAckCodec = {
  encode(message: ConnectAck, writer: _m0.Writer = _m0.Writer.create()): _m0.Writer {
    if (message.code !== 0) writer.uint32(8).int32(message.code);
    if (message.tenantId !== "") writer.uint32(18).string(message.tenantId);
    if (message.userId !== "") writer.uint32(26).string(message.userId);
    if (message.deviceId !== "") writer.uint32(34).string(message.deviceId);
    if (message.nodeId !== "") writer.uint32(42).string(message.nodeId);
    if (message.traceId !== "") writer.uint32(50).string(message.traceId);
    return writer;
  },
  decode(input: _m0.Reader | Uint8Array, length?: number): ConnectAck {
    const reader = input instanceof _m0.Reader ? input : new _m0.Reader(input);
    const end = length === undefined ? reader.len : reader.pos + length;
    const message = createBaseConnectAck();
    while (reader.pos < end) {
      const tag = reader.uint32();
      switch (tag >>> 3) {
        case 1:
          message.code = reader.int32();
          break;
        case 2:
          message.tenantId = reader.string();
          break;
        case 3:
          message.userId = reader.string();
          break;
        case 4:
          message.deviceId = reader.string();
          break;
        case 5:
          message.nodeId = reader.string();
          break;
        case 6:
          message.traceId = reader.string();
          break;
        default:
          reader.skipType(tag & 7);
          break;
      }
    }
    return message;
  }
};

function createBaseMessageDelivery(): MessageDelivery {
  return { message: undefined };
}

export const MessageDeliveryCodec = {
  encode(message: MessageDelivery, writer: _m0.Writer = _m0.Writer.create()): _m0.Writer {
    if (message.message !== undefined) MessageRequestCodec.encode(message.message, writer.uint32(10).fork()).ldelim();
    return writer;
  },
  decode(input: _m0.Reader | Uint8Array, length?: number): MessageDelivery {
    const reader = input instanceof _m0.Reader ? input : new _m0.Reader(input);
    const end = length === undefined ? reader.len : reader.pos + length;
    const message = createBaseMessageDelivery();
    while (reader.pos < end) {
      const tag = reader.uint32();
      switch (tag >>> 3) {
        case 1:
          message.message = MessageRequestCodec.decode(reader, reader.uint32());
          break;
        default:
          reader.skipType(tag & 7);
          break;
      }
    }
    return message;
  }
};

function createBaseServerError(): ServerError {
  return { code: 0, message: "" };
}

export const ServerErrorCodec = {
  encode(message: ServerError, writer: _m0.Writer = _m0.Writer.create()): _m0.Writer {
    if (message.code !== 0) writer.uint32(8).int32(message.code);
    if (message.message !== "") writer.uint32(18).string(message.message);
    return writer;
  },
  decode(input: _m0.Reader | Uint8Array, length?: number): ServerError {
    const reader = input instanceof _m0.Reader ? input : new _m0.Reader(input);
    const end = length === undefined ? reader.len : reader.pos + length;
    const message = createBaseServerError();
    while (reader.pos < end) {
      const tag = reader.uint32();
      switch (tag >>> 3) {
        case 1:
          message.code = reader.int32();
          break;
        case 2:
          message.message = reader.string();
          break;
        default:
          reader.skipType(tag & 7);
          break;
      }
    }
    return message;
  }
};

function createBaseHeartbeatPing(): HeartbeatPing {
  return { timestampMs: 0 };
}

export const HeartbeatPingCodec = {
  encode(message: HeartbeatPing, writer: _m0.Writer = _m0.Writer.create()): _m0.Writer {
    if (message.timestampMs !== 0) writer.uint32(8).int64(message.timestampMs);
    return writer;
  },
  decode(input: _m0.Reader | Uint8Array, length?: number): HeartbeatPing {
    const reader = input instanceof _m0.Reader ? input : new _m0.Reader(input);
    const end = length === undefined ? reader.len : reader.pos + length;
    const message = createBaseHeartbeatPing();
    while (reader.pos < end) {
      const tag = reader.uint32();
      switch (tag >>> 3) {
        case 1:
          message.timestampMs = longToNumber(reader.int64() as any);
          break;
        default:
          reader.skipType(tag & 7);
          break;
      }
    }
    return message;
  }
};

function createBaseHeartbeatPong(): HeartbeatPong {
  return { timestampMs: 0 };
}

export const HeartbeatPongCodec = {
  encode(message: HeartbeatPong, writer: _m0.Writer = _m0.Writer.create()): _m0.Writer {
    if (message.timestampMs !== 0) writer.uint32(8).int64(message.timestampMs);
    return writer;
  },
  decode(input: _m0.Reader | Uint8Array, length?: number): HeartbeatPong {
    const reader = input instanceof _m0.Reader ? input : new _m0.Reader(input);
    const end = length === undefined ? reader.len : reader.pos + length;
    const message = createBaseHeartbeatPong();
    while (reader.pos < end) {
      const tag = reader.uint32();
      switch (tag >>> 3) {
        case 1:
          message.timestampMs = longToNumber(reader.int64() as any);
          break;
        default:
          reader.skipType(tag & 7);
          break;
      }
    }
    return message;
  }
};

export const ClientFrameCodec = {
  encode(message: ClientFrame, writer: _m0.Writer = _m0.Writer.create()): _m0.Writer {
    if (message.payload === "message") MessageRequestCodec.encode(message.message, writer.uint32(10).fork()).ldelim();
    if (message.payload === "heartbeatPing") HeartbeatPingCodec.encode(message.heartbeatPing, writer.uint32(18).fork()).ldelim();
    return writer;
  },
  decode(input: _m0.Reader | Uint8Array, length?: number): ClientFrame {
    const reader = input instanceof _m0.Reader ? input : new _m0.Reader(input);
    const end = length === undefined ? reader.len : reader.pos + length;
    let msg: MessageRequest | undefined;
    let hb: HeartbeatPing | undefined;
    while (reader.pos < end) {
      const tag = reader.uint32();
      switch (tag >>> 3) {
        case 1:
          msg = MessageRequestCodec.decode(reader, reader.uint32());
          break;
        case 2:
          hb = HeartbeatPingCodec.decode(reader, reader.uint32());
          break;
        default:
          reader.skipType(tag & 7);
          break;
      }
    }
    if (msg) return { payload: "message", message: msg };
    if (hb) return { payload: "heartbeatPing", heartbeatPing: hb };
    return { payload: undefined };
  }
};

export const ServerFrameCodec = {
  encode(message: ServerFrame, writer: _m0.Writer = _m0.Writer.create()): _m0.Writer {
    if (message.payload === "connectAck") ConnectAckCodec.encode(message.connectAck, writer.uint32(10).fork()).ldelim();
    if (message.payload === "delivery") MessageDeliveryCodec.encode(message.delivery, writer.uint32(18).fork()).ldelim();
    if (message.payload === "ack") MessageAckCodec.encode(message.ack, writer.uint32(26).fork()).ldelim();
    if (message.payload === "error") ServerErrorCodec.encode(message.error, writer.uint32(34).fork()).ldelim();
    if (message.payload === "heartbeatPong") HeartbeatPongCodec.encode(message.heartbeatPong, writer.uint32(42).fork()).ldelim();
    return writer;
  },
  decode(input: _m0.Reader | Uint8Array, length?: number): ServerFrame {
    const reader = input instanceof _m0.Reader ? input : new _m0.Reader(input);
    const end = length === undefined ? reader.len : reader.pos + length;
    let connectAck: ConnectAck | undefined;
    let delivery: MessageDelivery | undefined;
    let ack: MessageAck | undefined;
    let err: ServerError | undefined;
    let pong: HeartbeatPong | undefined;
    while (reader.pos < end) {
      const tag = reader.uint32();
      switch (tag >>> 3) {
        case 1:
          connectAck = ConnectAckCodec.decode(reader, reader.uint32());
          break;
        case 2:
          delivery = MessageDeliveryCodec.decode(reader, reader.uint32());
          break;
        case 3:
          ack = MessageAckCodec.decode(reader, reader.uint32());
          break;
        case 4:
          err = ServerErrorCodec.decode(reader, reader.uint32());
          break;
        case 5:
          pong = HeartbeatPongCodec.decode(reader, reader.uint32());
          break;
        default:
          reader.skipType(tag & 7);
          break;
      }
    }
    if (connectAck) return { payload: "connectAck", connectAck };
    if (delivery) return { payload: "delivery", delivery };
    if (ack) return { payload: "ack", ack };
    if (err) return { payload: "error", error: err };
    if (pong) return { payload: "heartbeatPong", heartbeatPong: pong };
    return { payload: undefined };
  }
};

function longToNumber(x: any): number {
  if (typeof x === "number") return x;
  return x.toNumber();
}
