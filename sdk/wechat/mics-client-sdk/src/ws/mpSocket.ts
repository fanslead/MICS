export interface MpSocket {
  send(data: Uint8Array): Promise<void>;
  close(code?: number, reason?: string): void;
  onOpen(cb: () => void): void;
  onMessage(cb: (bytes: Uint8Array) => void): void;
  onClose(cb: (ev: { code?: number; reason?: string }) => void): void;
  onError(cb: (err: unknown) => void): void;
}

type WxSocketTask = {
  send(options: { data: ArrayBuffer }): void;
  close(options?: { code?: number; reason?: string }): void;
  onOpen(cb: () => void): void;
  onMessage(cb: (ev: { data: ArrayBuffer | string }) => void): void;
  onClose(cb: (ev: { code?: number; reason?: string }) => void): void;
  onError(cb: (ev: unknown) => void): void;
};

type WxApi = {
  connectSocket(options: { url: string; success?: () => void; fail?: (e: unknown) => void }): WxSocketTask;
};

export function createWxMpSocket(url: string): MpSocket {
  const wxApi = (globalThis as any).wx as WxApi | undefined;
  if (!wxApi?.connectSocket) {
    throw new Error("wx.connectSocket is not available (are you running in WeChat Mini Program?)");
  }

  const task = wxApi.connectSocket({ url });
  return new WxMpSocket(task);
}

class WxMpSocket implements MpSocket {
  constructor(private readonly task: WxSocketTask) {}

  async send(data: Uint8Array): Promise<void> {
    const b = data.buffer;
    let buf: ArrayBuffer;
    if (b instanceof ArrayBuffer) {
      buf = b.slice(data.byteOffset, data.byteOffset + data.byteLength);
    } else {
      const copy = new Uint8Array(data.byteLength);
      copy.set(data);
      buf = copy.buffer;
    }
    this.task.send({ data: buf });
  }

  close(code?: number, reason?: string): void {
    this.task.close({ code, reason });
  }

  onOpen(cb: () => void): void {
    this.task.onOpen(cb);
  }

  onMessage(cb: (bytes: Uint8Array) => void): void {
    this.task.onMessage((ev) => {
      if (typeof ev.data === "string") return;
      cb(new Uint8Array(ev.data));
    });
  }

  onClose(cb: (ev: { code?: number; reason?: string }) => void): void {
    this.task.onClose(cb);
  }

  onError(cb: (err: unknown) => void): void {
    this.task.onError(cb);
  }
}
