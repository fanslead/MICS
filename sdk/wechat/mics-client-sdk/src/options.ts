import type { MessageCrypto } from "./crypto/types";

export interface MicsClientOptions {
  connectTimeoutMs: number;
  ackTimeoutMs: number;
  maxSendAttempts: number;
  heartbeatIntervalMs: number;
  autoReconnect: boolean;
  reconnectMinDelayMs: number;
  reconnectMaxDelayMs: number;
  messageCrypto?: MessageCrypto;
}

export const MicsClientOptions = {
  default(overrides: Partial<MicsClientOptions> = {}): MicsClientOptions {
    return {
      connectTimeoutMs: 5000,
      ackTimeoutMs: 3000,
      maxSendAttempts: 3,
      heartbeatIntervalMs: 10_000,
      autoReconnect: true,
      reconnectMinDelayMs: 200,
      reconnectMaxDelayMs: 5000,
      ...overrides
    };
  }
};

