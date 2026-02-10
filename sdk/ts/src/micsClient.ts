import { buildWsUrl } from "./wsUriBuilder";
import { MicsClientOptions, type MicsClientOptions as MicsClientOptionsType } from "./options";
import type {
  ConnectParams,
  MicsClientCallbacks,
  MicsClientState,
  MicsSession,
  SendGroupChatParams,
  SendSingleChatParams
} from "./types";
import {
  AckStatus,
  ClientFrameCodec,
  type MessageAck,
  type MessageDelivery,
  type MessageRequest,
  MessageType,
  type ServerError,
  ServerFrameCodec
} from "./proto/mics_message";

type WebSocketFactory = (url: string) => WebSocket;

function nowMs(): number {
  return Date.now();
}

function sleep(ms: number, signal?: AbortSignal): Promise<void> {
  if (ms <= 0) return Promise.resolve();
  return new Promise((resolve, reject) => {
    const id = setTimeout(resolve, ms);
    const onAbort = () => {
      clearTimeout(id);
      reject(new DOMException("Aborted", "AbortError"));
    };
    if (signal) {
      if (signal.aborted) return onAbort();
      signal.addEventListener("abort", onAbort, { once: true });
    }
  });
}

export class MicsClient {
  private readonly options: MicsClientOptionsType;
  private readonly wsFactory: WebSocketFactory;

  private state: MicsClientState = "disconnected";
  private callbacks: MicsClientCallbacks = {};

  private ws: WebSocket | null = null;
  private session: MicsSession | null = null;
  private connectParams: ConnectParams | null = null;

  private connectAckResolve: ((s: MicsSession) => void) | null = null;
  private connectAckReject: ((e: unknown) => void) | null = null;

  private heartbeatTimer: number | null = null;

  private reconnectTask: Promise<void> | null = null;
  private connectedWaiters: Array<() => void> = [];

  private pendingAcks = new Map<string, { resolve: (ack: MessageAck) => void }>();
  private nextMsgId = 0;

  constructor(options: MicsClientOptionsType = MicsClientOptions.default(), deps?: { wsFactory?: WebSocketFactory } & MicsClientCallbacks) {
    this.options = options;
    this.wsFactory = deps?.wsFactory ?? ((url) => new WebSocket(url));
    this.callbacks = deps ?? {};
  }

  set onStateChanged(cb: ((s: MicsClientState) => void) | undefined) {
    this.callbacks.onStateChanged = cb;
  }
  set onConnected(cb: ((s: MicsSession) => void) | undefined) {
    this.callbacks.onConnected = cb;
  }
  set onDeliveryReceived(cb: ((d: MessageDelivery) => void) | undefined) {
    this.callbacks.onDeliveryReceived = cb;
  }
  set onAckReceived(cb: ((a: MessageAck) => void) | undefined) {
    this.callbacks.onAckReceived = cb;
  }
  set onServerErrorReceived(cb: ((e: ServerError) => void) | undefined) {
    this.callbacks.onServerErrorReceived = cb;
  }

  getState(): MicsClientState {
    return this.state;
  }

  async connect(params: ConnectParams): Promise<MicsSession> {
    if (this.state !== "disconnected") {
      throw new Error("client is not disconnected");
    }

    this.connectParams = params;
    this.setState("connecting");

    const wsUrl = buildWsUrl(params.url, params.tenantId, params.token, params.deviceId);
    const ws = this.wsFactory(wsUrl);
    this.ws = ws;
    ws.binaryType = "arraybuffer";

    const sessionPromise = new Promise<MicsSession>((resolve, reject) => {
      this.connectAckResolve = resolve;
      this.connectAckReject = reject;
    });

    ws.onopen = () => {
      // wait for ConnectAck from server frames
    };
    ws.onmessage = (ev) => void this.handleMessage(ev.data);
    ws.onclose = () => void this.handleClose();
    ws.onerror = () => {
      // ws.onerror in browser does not carry details; rely on onclose
    };

    const timeout = this.options.connectTimeoutMs;
    const session = await Promise.race([
      sessionPromise,
      (async () => {
        await sleep(timeout);
        throw new Error("connect timeout");
      })()
    ]);

    this.session = session;
    this.setState("connected");
    this.startHeartbeat();
    this.callbacks.onConnected?.(session);
    this.signalConnected();
    return session;
  }

  async disconnect(): Promise<void> {
    this.setState("disposing");
    this.stopHeartbeat();
    try {
      this.ws?.close(1000, "dispose");
    } catch {
      // ignore
    }
    this.ws = null;
    this.session = null;
    this.setState("disconnected");
  }

  async sendSingleChat(params: SendSingleChatParams): Promise<MessageAck> {
    const msgId = params.msgId && params.msgId.length > 0 ? params.msgId : this.nextId();
    const { session } = await this.waitConnectedPair();

    const body = await this.prepareOutboundBody(params.msgBody);
    const msg: MessageRequest = {
      tenantId: session.tenantId,
      userId: session.userId,
      deviceId: session.deviceId,
      msgId,
      msgType: MessageType.SINGLE_CHAT,
      toUserId: params.toUserId,
      groupId: "",
      msgBody: body,
      timestampMs: nowMs()
    };

    return this.sendWithRetry(msg);
  }

  async sendGroupChat(params: SendGroupChatParams): Promise<MessageAck> {
    const msgId = params.msgId && params.msgId.length > 0 ? params.msgId : this.nextId();
    const { session } = await this.waitConnectedPair();

    const body = await this.prepareOutboundBody(params.msgBody);
    const msg: MessageRequest = {
      tenantId: session.tenantId,
      userId: session.userId,
      deviceId: session.deviceId,
      msgId,
      msgType: MessageType.GROUP_CHAT,
      toUserId: "",
      groupId: params.groupId,
      msgBody: body,
      timestampMs: nowMs()
    };

    return this.sendWithRetry(msg);
  }

  private async sendWithRetry(msg: MessageRequest): Promise<MessageAck> {
    let ackSettled = false;
    let ackValue: MessageAck | null = null;

    const ackPromise = new Promise<MessageAck>((resolve) =>
      this.pendingAcks.set(msg.msgId, {
        resolve: (ack) => {
          if (ackSettled) return;
          ackSettled = true;
          ackValue = ack;
          resolve(ack);
        }
      })
    );

    let sentAttempts = 0;
    const maxSendAttempts = Math.max(1, this.options.maxSendAttempts);

    try {
      while (sentAttempts < maxSendAttempts) {
        if (ackSettled && ackValue) return ackValue;

        const { ws } = await this.waitConnectedPair();
        msg.timestampMs = nowMs();
        const frameBytes = ClientFrameCodec.encode({ payload: "message", message: msg }).finish();

        try {
          if (!this.ws || this.ws.readyState !== WebSocket.OPEN) {
            throw new Error("ws not open");
          }
          wsSendBinary(ws, frameBytes);
          sentAttempts++;
        } catch {
          this.tryStartReconnect();
          continue;
        }

        const ackWait = await Promise.race([
          ackPromise.then((ack) => ({ kind: "ack" as const, ack })),
          sleep(this.options.ackTimeoutMs).then(() => ({ kind: "timeout" as const }))
        ]);

        if (ackWait.kind === "ack") return ackWait.ack;
      }

      return { msgId: msg.msgId, status: AckStatus.FAILED, timestampMs: nowMs(), reason: "ack timeout" };
    } finally {
      this.pendingAcks.delete(msg.msgId);
    }
  }

  private async handleMessage(data: unknown): Promise<void> {
    const bytes = await toUint8Array(data);
    if (bytes.byteLength === 0) return;

    let frame: any;
    try {
      frame = ServerFrameCodec.decode(bytes);
    } catch {
      return;
    }

    switch (frame.payload) {
      case "connectAck": {
        const ack = frame.connectAck;
        if (ack.code !== 1000) {
          this.connectAckReject?.(new Error("connect failed: " + ack.code));
          return;
        }
        this.connectAckResolve?.({
          tenantId: ack.tenantId ?? this.connectParams?.tenantId ?? "",
          userId: ack.userId ?? "",
          deviceId: ack.deviceId ?? this.connectParams?.deviceId ?? "",
          nodeId: ack.nodeId ?? "",
          traceId: ack.traceId ?? ""
        });
        return;
      }
      case "ack": {
        const ack: MessageAck = frame.ack;
        this.pendingAcks.get(ack.msgId)?.resolve(ack);
        this.callbacks.onAckReceived?.(ack);
        return;
      }
      case "delivery": {
        let delivery: MessageDelivery = frame.delivery;
        const crypto = this.options.messageCrypto;
        if (crypto && delivery.message && delivery.message.msgBody?.byteLength) {
          try {
            const dec = await crypto.decrypt(delivery.message.msgBody);
            delivery = { message: { ...delivery.message, msgBody: dec } };
          } catch {
            // best-effort: surface ciphertext if decrypt fails
          }
        }
        this.callbacks.onDeliveryReceived?.(delivery);
        return;
      }
      case "error": {
        this.callbacks.onServerErrorReceived?.(frame.error as ServerError);
        return;
      }
      case "heartbeatPong":
        return;
      default:
        return;
    }
  }

  private handleClose(): void {
    this.stopHeartbeat();
    this.ws = null;
    this.session = null;
    if (this.state === "disposing") return;
    this.tryStartReconnect();
  }

  private setState(state: MicsClientState): void {
    if (this.state === state) return;
    this.state = state;
    this.callbacks.onStateChanged?.(state);
  }

  private startHeartbeat(): void {
    this.stopHeartbeat();
    const interval = this.options.heartbeatIntervalMs;
    if (!interval || interval <= 0) return;

    this.heartbeatTimer = setInterval(() => {
      if (!this.ws || this.ws.readyState !== WebSocket.OPEN) return;
      const frame = ClientFrameCodec.encode({ payload: "heartbeatPing", heartbeatPing: { timestampMs: nowMs() } }).finish();
      try {
        wsSendBinary(this.ws, frame);
      } catch {
        // ignore
      }
    }, interval) as any;
  }

  private stopHeartbeat(): void {
    if (this.heartbeatTimer !== null) {
      clearInterval(this.heartbeatTimer);
      this.heartbeatTimer = null;
    }
  }

  private nextId(): string {
    this.nextMsgId++;
    return String(this.nextMsgId);
  }

  private async prepareOutboundBody(plaintext: Uint8Array): Promise<Uint8Array> {
    if (!plaintext || plaintext.byteLength === 0) return new Uint8Array();
    const crypto = this.options.messageCrypto;
    if (!crypto) return plaintext;
    return crypto.encrypt(plaintext);
  }

  private async waitConnectedPair(): Promise<{ ws: WebSocket; session: MicsSession }> {
    while (true) {
      if (this.state === "connected" && this.ws && this.ws.readyState === WebSocket.OPEN && this.session) {
        return { ws: this.ws, session: this.session };
      }

      if (this.state === "disconnected" || this.state === "disposing") {
        throw new Error("client is not connected");
      }

      if (!this.options.autoReconnect) {
        throw new Error("client is not connected");
      }

      await new Promise<void>((resolve) => this.connectedWaiters.push(resolve));
    }
  }

  private signalConnected(): void {
    const waiters = this.connectedWaiters;
    this.connectedWaiters = [];
    for (const w of waiters) w();
  }

  private tryStartReconnect(): void {
    if (!this.options.autoReconnect) {
      this.setState("disconnected");
      return;
    }
    if (this.reconnectTask) return;
    if (!this.connectParams) {
      this.setState("disconnected");
      return;
    }

    this.setState("reconnecting");
    const p = this.connectParams;

    this.reconnectTask = (async () => {
      let delay = Math.max(0, this.options.reconnectMinDelayMs);
      const max = Math.max(delay, this.options.reconnectMaxDelayMs);

      while (true) {
        try {
          // reset connect-ack promise hooks
          this.connectAckResolve = null;
          this.connectAckReject = null;
          this.session = null;

          const wsUrl = buildWsUrl(p.url, p.tenantId, p.token, p.deviceId);
          const ws = this.wsFactory(wsUrl);
          this.ws = ws;
          ws.binaryType = "arraybuffer";

          const sessionPromise = new Promise<MicsSession>((resolve, reject) => {
            this.connectAckResolve = resolve;
            this.connectAckReject = reject;
          });

          ws.onmessage = (ev) => void this.handleMessage(ev.data);
          ws.onclose = () => void this.handleClose();
          ws.onerror = () => {};

          // wait open then ack
          await new Promise<void>((resolve, reject) => {
            const to = setTimeout(() => reject(new Error("connect timeout")), this.options.connectTimeoutMs);
            ws.onopen = () => {
              clearTimeout(to);
              resolve();
            };
          });

          const session = await Promise.race([
            sessionPromise,
            (async () => {
              await sleep(this.options.connectTimeoutMs);
              throw new Error("connect timeout");
            })()
          ]);

          this.session = session;
          this.setState("connected");
          this.startHeartbeat();
          this.callbacks.onConnected?.(session);
          this.signalConnected();
          return;
        } catch {
          if (delay > 0) await sleep(delay);
          delay = Math.min(max, delay === 0 ? 50 : delay * 2);
        }
      }
    })().finally(() => {
      this.reconnectTask = null;
    });
  }
}

function wsSendBinary(ws: WebSocket, bytes: Uint8Array): void {
  ws.send(bytes);
}

async function toUint8Array(data: unknown): Promise<Uint8Array> {
  if (data instanceof ArrayBuffer) return new Uint8Array(data);
  if (ArrayBuffer.isView(data)) return new Uint8Array(data.buffer, data.byteOffset, data.byteLength);
  if (typeof Blob !== "undefined" && data instanceof Blob) return new Uint8Array(await data.arrayBuffer());
  if (data instanceof Uint8Array) return data;
  return new Uint8Array();
}
