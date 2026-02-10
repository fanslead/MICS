import type { MessageAck, MessageDelivery, ServerError } from "./proto/mics_message";

export type MicsClientState = "disconnected" | "connecting" | "connected" | "reconnecting" | "disposing";

export interface MicsSession {
  tenantId: string;
  userId: string;
  deviceId: string;
  nodeId: string;
  traceId: string;
}

export interface ConnectParams {
  url: string;
  tenantId: string;
  token: string;
  deviceId: string;
}

export interface SendSingleChatParams {
  toUserId: string;
  msgBody: Uint8Array;
  msgId?: string;
}

export interface SendGroupChatParams {
  groupId: string;
  msgBody: Uint8Array;
  msgId?: string;
}

export interface MicsClientCallbacks {
  onStateChanged?: (state: MicsClientState) => void;
  onConnected?: (session: MicsSession) => void;
  onDeliveryReceived?: (delivery: MessageDelivery) => void;
  onAckReceived?: (ack: MessageAck) => void;
  onServerErrorReceived?: (err: ServerError) => void;
}

