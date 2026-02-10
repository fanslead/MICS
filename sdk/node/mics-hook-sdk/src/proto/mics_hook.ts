import _m0 from "protobufjs/minimal.js";
import { MessageRequestCodec, type MessageRequest } from "./mics_message.js";

export enum EventType {
  CONNECT_ONLINE = 0,
  CONNECT_OFFLINE = 1,
  SINGLE_CHAT_MSG = 2,
  GROUP_CHAT_MSG = 3,
  OFFLINE_MESSAGE = 4,
  UNRECOGNIZED = -1,
}

export interface HookMeta {
  tenantId: string;
  requestId: string;
  timestampMs: number;
  sign: string;
  traceId: string;
}

export interface TenantRuntimeConfig {
  hookBaseUrl: string;
  heartbeatTimeoutSeconds: number;
  offlineBufferTtlSeconds: number;
  tenantMaxConnections: number;
  userMaxConnections: number;
  tenantMaxMessageQps: number;
  tenantSecret: string;

  hookMaxConcurrency?: number;
  hookQueueTimeoutMs?: number;
  hookBreakerFailureThreshold?: number;
  hookBreakerOpenMs?: number;
  hookSignRequired?: boolean;
  offlineUseHookPull?: boolean;
}

export interface AuthRequest {
  meta?: HookMeta;
  token: string;
  deviceId: string;
}

export interface AuthResponse {
  meta?: HookMeta;
  ok: boolean;
  userId: string;
  deviceId: string;
  config?: TenantRuntimeConfig;
  reason: string;
}

export interface CheckMessageRequest {
  meta?: HookMeta;
  message?: MessageRequest;
}

export interface CheckMessageResponse {
  meta?: HookMeta;
  allow: boolean;
  reason: string;
}

export interface GetGroupMembersRequest {
  meta?: HookMeta;
  groupId: string;
}

export interface GetGroupMembersResponse {
  meta?: HookMeta;
  userIds: string[];
}

export interface GetOfflineMessagesRequest {
  meta?: HookMeta;
  userId: string;
  deviceId: string;
  maxMessages: number;
  cursor: string;
}

export interface GetOfflineMessagesResponse {
  meta?: HookMeta;
  ok: boolean;
  messages: MessageRequest[];
  reason: string;
  nextCursor: string;
  hasMore: boolean;
}

export interface MqEvent {
  tenantId: string;
  eventType: EventType;
  msgId: string;
  userId: string;
  deviceId: string;
  toUserId: string;
  groupId: string;
  eventData: Uint8Array;
  timestamp: number;
  nodeId: string;
  sign: string;
  traceId: string;
}

function createBaseHookMeta(): HookMeta {
  return { tenantId: "", requestId: "", timestampMs: 0, sign: "", traceId: "" };
}

export const HookMetaCodec = {
  encode(message: HookMeta, writer: _m0.Writer = _m0.Writer.create()): _m0.Writer {
    if (message.tenantId !== "") writer.uint32(10).string(message.tenantId);
    if (message.requestId !== "") writer.uint32(18).string(message.requestId);
    if (message.timestampMs !== 0) writer.uint32(24).int64(message.timestampMs);
    if (message.sign !== "") writer.uint32(34).string(message.sign);
    if (message.traceId !== "") writer.uint32(42).string(message.traceId);
    return writer;
  },
  decode(input: _m0.Reader | Uint8Array, length?: number): HookMeta {
    const reader = input instanceof _m0.Reader ? input : new _m0.Reader(input);
    const end = length === undefined ? reader.len : reader.pos + length;
    const message = createBaseHookMeta();
    while (reader.pos < end) {
      const tag = reader.uint32();
      switch (tag >>> 3) {
        case 1:
          message.tenantId = reader.string();
          break;
        case 2:
          message.requestId = reader.string();
          break;
        case 3:
          message.timestampMs = longToNumber(reader.int64() as any);
          break;
        case 4:
          message.sign = reader.string();
          break;
        case 5:
          message.traceId = reader.string();
          break;
        default:
          reader.skipType(tag & 7);
          break;
      }
    }
    return message;
  },
};

function createBaseTenantRuntimeConfig(): TenantRuntimeConfig {
  return {
    hookBaseUrl: "",
    heartbeatTimeoutSeconds: 0,
    offlineBufferTtlSeconds: 0,
    tenantMaxConnections: 0,
    userMaxConnections: 0,
    tenantMaxMessageQps: 0,
    tenantSecret: "",
    hookMaxConcurrency: undefined,
    hookQueueTimeoutMs: undefined,
    hookBreakerFailureThreshold: undefined,
    hookBreakerOpenMs: undefined,
    hookSignRequired: undefined,
    offlineUseHookPull: undefined,
  };
}

export const TenantRuntimeConfigCodec = {
  encode(message: TenantRuntimeConfig, writer: _m0.Writer = _m0.Writer.create()): _m0.Writer {
    if (message.hookBaseUrl !== "") writer.uint32(10).string(message.hookBaseUrl);
    if (message.heartbeatTimeoutSeconds !== 0) writer.uint32(16).int32(message.heartbeatTimeoutSeconds);
    if (message.offlineBufferTtlSeconds !== 0) writer.uint32(24).int32(message.offlineBufferTtlSeconds);
    if (message.tenantMaxConnections !== 0) writer.uint32(32).int32(message.tenantMaxConnections);
    if (message.userMaxConnections !== 0) writer.uint32(40).int32(message.userMaxConnections);
    if (message.tenantMaxMessageQps !== 0) writer.uint32(48).int32(message.tenantMaxMessageQps);
    if (message.tenantSecret !== "") writer.uint32(58).string(message.tenantSecret);

    if (message.hookMaxConcurrency !== undefined) writer.uint32(64).int32(message.hookMaxConcurrency);
    if (message.hookQueueTimeoutMs !== undefined) writer.uint32(72).int32(message.hookQueueTimeoutMs);
    if (message.hookBreakerFailureThreshold !== undefined) writer.uint32(80).int32(message.hookBreakerFailureThreshold);
    if (message.hookBreakerOpenMs !== undefined) writer.uint32(88).int32(message.hookBreakerOpenMs);
    if (message.hookSignRequired !== undefined) writer.uint32(96).bool(message.hookSignRequired);
    if (message.offlineUseHookPull !== undefined) writer.uint32(104).bool(message.offlineUseHookPull);
    return writer;
  },
  decode(input: _m0.Reader | Uint8Array, length?: number): TenantRuntimeConfig {
    const reader = input instanceof _m0.Reader ? input : new _m0.Reader(input);
    const end = length === undefined ? reader.len : reader.pos + length;
    const message = createBaseTenantRuntimeConfig();
    while (reader.pos < end) {
      const tag = reader.uint32();
      switch (tag >>> 3) {
        case 1:
          message.hookBaseUrl = reader.string();
          break;
        case 2:
          message.heartbeatTimeoutSeconds = reader.int32();
          break;
        case 3:
          message.offlineBufferTtlSeconds = reader.int32();
          break;
        case 4:
          message.tenantMaxConnections = reader.int32();
          break;
        case 5:
          message.userMaxConnections = reader.int32();
          break;
        case 6:
          message.tenantMaxMessageQps = reader.int32();
          break;
        case 7:
          message.tenantSecret = reader.string();
          break;
        case 8:
          message.hookMaxConcurrency = reader.int32();
          break;
        case 9:
          message.hookQueueTimeoutMs = reader.int32();
          break;
        case 10:
          message.hookBreakerFailureThreshold = reader.int32();
          break;
        case 11:
          message.hookBreakerOpenMs = reader.int32();
          break;
        case 12:
          message.hookSignRequired = reader.bool();
          break;
        case 13:
          message.offlineUseHookPull = reader.bool();
          break;
        default:
          reader.skipType(tag & 7);
          break;
      }
    }
    return message;
  },
};

function createBaseAuthRequest(): AuthRequest {
  return { meta: undefined, token: "", deviceId: "" };
}

export const AuthRequestCodec = {
  encode(message: AuthRequest, writer: _m0.Writer = _m0.Writer.create()): _m0.Writer {
    if (message.meta !== undefined) HookMetaCodec.encode(message.meta, writer.uint32(10).fork()).ldelim();
    if (message.token !== "") writer.uint32(18).string(message.token);
    if (message.deviceId !== "") writer.uint32(26).string(message.deviceId);
    return writer;
  },
  decode(input: _m0.Reader | Uint8Array, length?: number): AuthRequest {
    const reader = input instanceof _m0.Reader ? input : new _m0.Reader(input);
    const end = length === undefined ? reader.len : reader.pos + length;
    const message = createBaseAuthRequest();
    while (reader.pos < end) {
      const tag = reader.uint32();
      switch (tag >>> 3) {
        case 1:
          message.meta = HookMetaCodec.decode(reader, reader.uint32());
          break;
        case 2:
          message.token = reader.string();
          break;
        case 3:
          message.deviceId = reader.string();
          break;
        default:
          reader.skipType(tag & 7);
          break;
      }
    }
    return message;
  },
};

function createBaseAuthResponse(): AuthResponse {
  return { meta: undefined, ok: false, userId: "", deviceId: "", config: undefined, reason: "" };
}

export const AuthResponseCodec = {
  encode(message: AuthResponse, writer: _m0.Writer = _m0.Writer.create()): _m0.Writer {
    if (message.meta !== undefined) HookMetaCodec.encode(message.meta, writer.uint32(10).fork()).ldelim();
    if (message.ok === true) writer.uint32(16).bool(message.ok);
    if (message.userId !== "") writer.uint32(26).string(message.userId);
    if (message.deviceId !== "") writer.uint32(34).string(message.deviceId);
    if (message.config !== undefined) TenantRuntimeConfigCodec.encode(message.config, writer.uint32(42).fork()).ldelim();
    if (message.reason !== "") writer.uint32(50).string(message.reason);
    return writer;
  },
  decode(input: _m0.Reader | Uint8Array, length?: number): AuthResponse {
    const reader = input instanceof _m0.Reader ? input : new _m0.Reader(input);
    const end = length === undefined ? reader.len : reader.pos + length;
    const message = createBaseAuthResponse();
    while (reader.pos < end) {
      const tag = reader.uint32();
      switch (tag >>> 3) {
        case 1:
          message.meta = HookMetaCodec.decode(reader, reader.uint32());
          break;
        case 2:
          message.ok = reader.bool();
          break;
        case 3:
          message.userId = reader.string();
          break;
        case 4:
          message.deviceId = reader.string();
          break;
        case 5:
          message.config = TenantRuntimeConfigCodec.decode(reader, reader.uint32());
          break;
        case 6:
          message.reason = reader.string();
          break;
        default:
          reader.skipType(tag & 7);
          break;
      }
    }
    return message;
  },
};

function createBaseCheckMessageRequest(): CheckMessageRequest {
  return { meta: undefined, message: undefined };
}

export const CheckMessageRequestCodec = {
  encode(message: CheckMessageRequest, writer: _m0.Writer = _m0.Writer.create()): _m0.Writer {
    if (message.meta !== undefined) HookMetaCodec.encode(message.meta, writer.uint32(10).fork()).ldelim();
    if (message.message !== undefined) MessageRequestCodec.encode(message.message, writer.uint32(18).fork()).ldelim();
    return writer;
  },
  decode(input: _m0.Reader | Uint8Array, length?: number): CheckMessageRequest {
    const reader = input instanceof _m0.Reader ? input : new _m0.Reader(input);
    const end = length === undefined ? reader.len : reader.pos + length;
    const message = createBaseCheckMessageRequest();
    while (reader.pos < end) {
      const tag = reader.uint32();
      switch (tag >>> 3) {
        case 1:
          message.meta = HookMetaCodec.decode(reader, reader.uint32());
          break;
        case 2:
          message.message = MessageRequestCodec.decode(reader, reader.uint32());
          break;
        default:
          reader.skipType(tag & 7);
          break;
      }
    }
    return message;
  },
};

function createBaseCheckMessageResponse(): CheckMessageResponse {
  return { meta: undefined, allow: false, reason: "" };
}

export const CheckMessageResponseCodec = {
  encode(message: CheckMessageResponse, writer: _m0.Writer = _m0.Writer.create()): _m0.Writer {
    if (message.meta !== undefined) HookMetaCodec.encode(message.meta, writer.uint32(10).fork()).ldelim();
    if (message.allow === true) writer.uint32(16).bool(message.allow);
    if (message.reason !== "") writer.uint32(26).string(message.reason);
    return writer;
  },
  decode(input: _m0.Reader | Uint8Array, length?: number): CheckMessageResponse {
    const reader = input instanceof _m0.Reader ? input : new _m0.Reader(input);
    const end = length === undefined ? reader.len : reader.pos + length;
    const message = createBaseCheckMessageResponse();
    while (reader.pos < end) {
      const tag = reader.uint32();
      switch (tag >>> 3) {
        case 1:
          message.meta = HookMetaCodec.decode(reader, reader.uint32());
          break;
        case 2:
          message.allow = reader.bool();
          break;
        case 3:
          message.reason = reader.string();
          break;
        default:
          reader.skipType(tag & 7);
          break;
      }
    }
    return message;
  },
};

function createBaseGetGroupMembersRequest(): GetGroupMembersRequest {
  return { meta: undefined, groupId: "" };
}

export const GetGroupMembersRequestCodec = {
  encode(message: GetGroupMembersRequest, writer: _m0.Writer = _m0.Writer.create()): _m0.Writer {
    if (message.meta !== undefined) HookMetaCodec.encode(message.meta, writer.uint32(10).fork()).ldelim();
    if (message.groupId !== "") writer.uint32(18).string(message.groupId);
    return writer;
  },
  decode(input: _m0.Reader | Uint8Array, length?: number): GetGroupMembersRequest {
    const reader = input instanceof _m0.Reader ? input : new _m0.Reader(input);
    const end = length === undefined ? reader.len : reader.pos + length;
    const message = createBaseGetGroupMembersRequest();
    while (reader.pos < end) {
      const tag = reader.uint32();
      switch (tag >>> 3) {
        case 1:
          message.meta = HookMetaCodec.decode(reader, reader.uint32());
          break;
        case 2:
          message.groupId = reader.string();
          break;
        default:
          reader.skipType(tag & 7);
          break;
      }
    }
    return message;
  },
};

function createBaseGetGroupMembersResponse(): GetGroupMembersResponse {
  return { meta: undefined, userIds: [] };
}

export const GetGroupMembersResponseCodec = {
  encode(message: GetGroupMembersResponse, writer: _m0.Writer = _m0.Writer.create()): _m0.Writer {
    if (message.meta !== undefined) HookMetaCodec.encode(message.meta, writer.uint32(10).fork()).ldelim();
    for (const v of message.userIds) writer.uint32(18).string(v!);
    return writer;
  },
  decode(input: _m0.Reader | Uint8Array, length?: number): GetGroupMembersResponse {
    const reader = input instanceof _m0.Reader ? input : new _m0.Reader(input);
    const end = length === undefined ? reader.len : reader.pos + length;
    const message = createBaseGetGroupMembersResponse();
    while (reader.pos < end) {
      const tag = reader.uint32();
      switch (tag >>> 3) {
        case 1:
          message.meta = HookMetaCodec.decode(reader, reader.uint32());
          break;
        case 2:
          message.userIds.push(reader.string());
          break;
        default:
          reader.skipType(tag & 7);
          break;
      }
    }
    return message;
  },
};

function createBaseGetOfflineMessagesRequest(): GetOfflineMessagesRequest {
  return { meta: undefined, userId: "", deviceId: "", maxMessages: 0, cursor: "" };
}

export const GetOfflineMessagesRequestCodec = {
  encode(message: GetOfflineMessagesRequest, writer: _m0.Writer = _m0.Writer.create()): _m0.Writer {
    if (message.meta !== undefined) HookMetaCodec.encode(message.meta, writer.uint32(10).fork()).ldelim();
    if (message.userId !== "") writer.uint32(18).string(message.userId);
    if (message.deviceId !== "") writer.uint32(26).string(message.deviceId);
    if (message.maxMessages !== 0) writer.uint32(32).int32(message.maxMessages);
    if (message.cursor !== "") writer.uint32(42).string(message.cursor);
    return writer;
  },
  decode(input: _m0.Reader | Uint8Array, length?: number): GetOfflineMessagesRequest {
    const reader = input instanceof _m0.Reader ? input : new _m0.Reader(input);
    const end = length === undefined ? reader.len : reader.pos + length;
    const message = createBaseGetOfflineMessagesRequest();
    while (reader.pos < end) {
      const tag = reader.uint32();
      switch (tag >>> 3) {
        case 1:
          message.meta = HookMetaCodec.decode(reader, reader.uint32());
          break;
        case 2:
          message.userId = reader.string();
          break;
        case 3:
          message.deviceId = reader.string();
          break;
        case 4:
          message.maxMessages = reader.int32();
          break;
        case 5:
          message.cursor = reader.string();
          break;
        default:
          reader.skipType(tag & 7);
          break;
      }
    }
    return message;
  },
};

function createBaseGetOfflineMessagesResponse(): GetOfflineMessagesResponse {
  return { meta: undefined, ok: false, messages: [], reason: "", nextCursor: "", hasMore: false };
}

export const GetOfflineMessagesResponseCodec = {
  encode(message: GetOfflineMessagesResponse, writer: _m0.Writer = _m0.Writer.create()): _m0.Writer {
    if (message.meta !== undefined) HookMetaCodec.encode(message.meta, writer.uint32(10).fork()).ldelim();
    if (message.ok === true) writer.uint32(16).bool(message.ok);
    for (const v of message.messages) MessageRequestCodec.encode(v!, writer.uint32(26).fork()).ldelim();
    if (message.reason !== "") writer.uint32(34).string(message.reason);
    if (message.nextCursor !== "") writer.uint32(42).string(message.nextCursor);
    if (message.hasMore === true) writer.uint32(48).bool(message.hasMore);
    return writer;
  },
  decode(input: _m0.Reader | Uint8Array, length?: number): GetOfflineMessagesResponse {
    const reader = input instanceof _m0.Reader ? input : new _m0.Reader(input);
    const end = length === undefined ? reader.len : reader.pos + length;
    const message = createBaseGetOfflineMessagesResponse();
    while (reader.pos < end) {
      const tag = reader.uint32();
      switch (tag >>> 3) {
        case 1:
          message.meta = HookMetaCodec.decode(reader, reader.uint32());
          break;
        case 2:
          message.ok = reader.bool();
          break;
        case 3:
          message.messages.push(MessageRequestCodec.decode(reader, reader.uint32()));
          break;
        case 4:
          message.reason = reader.string();
          break;
        case 5:
          message.nextCursor = reader.string();
          break;
        case 6:
          message.hasMore = reader.bool();
          break;
        default:
          reader.skipType(tag & 7);
          break;
      }
    }
    return message;
  },
};

function createBaseMqEvent(): MqEvent {
  return {
    tenantId: "",
    eventType: 0,
    msgId: "",
    userId: "",
    deviceId: "",
    toUserId: "",
    groupId: "",
    eventData: new Uint8Array(),
    timestamp: 0,
    nodeId: "",
    sign: "",
    traceId: "",
  };
}

export const MqEventCodec = {
  encode(message: MqEvent, writer: _m0.Writer = _m0.Writer.create()): _m0.Writer {
    if (message.tenantId !== "") writer.uint32(10).string(message.tenantId);
    if (message.eventType !== 0) writer.uint32(16).int32(message.eventType);
    if (message.msgId !== "") writer.uint32(26).string(message.msgId);
    if (message.userId !== "") writer.uint32(34).string(message.userId);
    if (message.deviceId !== "") writer.uint32(42).string(message.deviceId);
    if (message.toUserId !== "") writer.uint32(50).string(message.toUserId);
    if (message.groupId !== "") writer.uint32(58).string(message.groupId);
    if (message.eventData.length !== 0) writer.uint32(66).bytes(message.eventData);
    if (message.timestamp !== 0) writer.uint32(72).int64(message.timestamp);
    if (message.nodeId !== "") writer.uint32(82).string(message.nodeId);
    if (message.sign !== "") writer.uint32(90).string(message.sign);
    if (message.traceId !== "") writer.uint32(98).string(message.traceId);
    return writer;
  },
  decode(input: _m0.Reader | Uint8Array, length?: number): MqEvent {
    const reader = input instanceof _m0.Reader ? input : new _m0.Reader(input);
    const end = length === undefined ? reader.len : reader.pos + length;
    const message = createBaseMqEvent();
    while (reader.pos < end) {
      const tag = reader.uint32();
      switch (tag >>> 3) {
        case 1:
          message.tenantId = reader.string();
          break;
        case 2:
          message.eventType = reader.int32() as EventType;
          break;
        case 3:
          message.msgId = reader.string();
          break;
        case 4:
          message.userId = reader.string();
          break;
        case 5:
          message.deviceId = reader.string();
          break;
        case 6:
          message.toUserId = reader.string();
          break;
        case 7:
          message.groupId = reader.string();
          break;
        case 8:
          message.eventData = reader.bytes();
          break;
        case 9:
          message.timestamp = longToNumber(reader.int64() as any);
          break;
        case 10:
          message.nodeId = reader.string();
          break;
        case 11:
          message.sign = reader.string();
          break;
        case 12:
          message.traceId = reader.string();
          break;
        default:
          reader.skipType(tag & 7);
          break;
      }
    }
    return message;
  },
};

function longToNumber(x: any): number {
  if (typeof x === "number") return x;
  return x.toNumber();
}
