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
import type { MpSocket } from "./ws/mpSocket";
import { createWxMpSocket } from "./ws/mpSocket";

type SocketFactory = (url: string) => MpSocket;

function nowMs(): number {
  return Date.now();
}

function sleep(ms: number): Promise<void> {
  if (ms <= 0) return Promise.resolve();
  return new Promise((resolve) => setTimeout(resolve, ms));
}

export class MicsClient {
  private readonly options: MicsClientOptionsType;
  private readonly socketFactory: SocketFactory;

  private state: MicsClientState = "disconnected";
  private callbacks: MicsClientCallbacks = {};

  private sock: MpSocket | null = null;
  private session: MicsSession | null = null;
  private connectParams: ConnectParams | null = null;

  private connectAckResolve: ((s: MicsSession) => void) | null = null;
  private connectAckReject: ((e: unknown) => void) | null = null;

  private heartbeatTimer: number | null = null;

  private reconnectTask: Promise<void> | null = null;
  private connectedWaiters: Array<() => void> = [];

  private pendingAcks = new Map<string, { resolve: (ack: MessageAck) => void }>();
  private nextMsgId = 0;

  constructor(
    options: MicsClientOptionsType = MicsClientOptions.default(),
    deps?: { socketFactory?: SocketFactory } & MicsClientCallbacks
  ) {
    this.options = options;
    this.socketFactory = deps?.socketFactory ?? ((url) => createWxMpSocket(url));
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
    const sock = this.socketFactory(wsUrl);
    this.sock = sock;

    const sessionPromise = new Promise<MicsSession>((resolve, reject) => {
      this.connectAckResolve = resolve;
      this.connectAckReject = reject;
    });

    sock.onMessage((bytes) => void this.handleMessage(bytes));
    sock.onClose(() => void this.handleClose());
    sock.onError(() => {
      // rely on close for state changes
    });

    // wait for connect ack
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
      this.sock?.close(1000, "dispose");
    } catch {
      // ignore
    }
    this.sock = null;
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

        const { sock } = await this.waitConnectedPair();
        msg.timestampMs = nowMs();
        const frameBytes = ClientFrameCodec.encode({ payload: "message", message: msg }).finish();

        try {
          if (!this.sock) {
            throw new Error("ws not ready");
          }
          await sock.send(frameBytes);
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

  private async handleMessage(bytes: Uint8Array): Promise<void> {
    let frame: ReturnType<typeof ServerFrameCodec.decode>;
    try {
      frame = ServerFrameCodec.decode(bytes);
    } catch {
      return;
    }

    switch (frame.payload) {
      case "connectAck": {
        const ack = frame.connectAck;
        if (ack.code !== 1000) {
          this.connectAckReject?.(new Error(`connect rejected: ${ack.code}`));
          return;
        }
        const session: MicsSession = {
          tenantId: ack.tenantId,
          userId: ack.userId,
          deviceId: ack.deviceId,
          nodeId: ack.nodeId,
          traceId: ack.traceId
        };
        this.connectAckResolve?.(session);
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
    this.sock = null;
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
      if (!this.sock) return;
      const frame = ClientFrameCodec.encode({ payload: "heartbeatPing", heartbeatPing: { timestampMs: nowMs() } }).finish();
      void this.sock.send(frame);
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

  private async waitConnectedPair(): Promise<{ sock: MpSocket; session: MicsSession }> {
    while (true) {
      if (this.state === "connected" && this.sock && this.session) {
        return { sock: this.sock, session: this.session };
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
          this.connectAckResolve = null;
          this.connectAckReject = null;
          this.session = null;

          const wsUrl = buildWsUrl(p.url, p.tenantId, p.token, p.deviceId);
          const sock = this.socketFactory(wsUrl);
          this.sock = sock;

          const sessionPromise = new Promise<MicsSession>((resolve, reject) => {
            this.connectAckResolve = resolve;
            this.connectAckReject = reject;
          });

          sock.onMessage((bytes) => void this.handleMessage(bytes));
          sock.onClose(() => void this.handleClose());
          sock.onError(() => {});

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

