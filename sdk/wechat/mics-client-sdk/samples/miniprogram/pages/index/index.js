const { MicsClient, MicsClientOptions } = require("@mics/wechat-mp-sdk");

function appendLog(page, line) {
  const next = (page.data.log ? page.data.log + "\n" : "") + line;
  page.setData({ log: next.slice(-4000) });
}

Page({
  data: {
    url: "ws://localhost:8080/ws",
    tenantId: "t1",
    token: "valid:u1",
    deviceId: "dev1",
    toUserId: "u2",
    message: "hello from miniprogram",
    log: ""
  },

  onLoad() {
    const client = new MicsClient(MicsClientOptions.default());
    this._client = client;

    client.onStateChanged = (s) => appendLog(this, `state=${s}`);
    client.onConnected = (s) => appendLog(this, `connected user=${s.userId} node=${s.nodeId} traceId=${s.traceId}`);
    client.onDeliveryReceived = (d) => appendLog(this, `delivery msgId=${d.message && d.message.msgId}`);
    client.onAckReceived = (a) => appendLog(this, `ack msgId=${a.msgId} status=${a.status} reason=${a.reason || ""}`);
    client.onServerErrorReceived = (e) => appendLog(this, `server_error code=${e.code} message=${e.message}`);
  },

  onUrlInput(e) { this.setData({ url: e.detail.value }); },
  onTenantInput(e) { this.setData({ tenantId: e.detail.value }); },
  onTokenInput(e) { this.setData({ token: e.detail.value }); },
  onDeviceInput(e) { this.setData({ deviceId: e.detail.value }); },
  onToUserInput(e) { this.setData({ toUserId: e.detail.value }); },
  onMessageInput(e) { this.setData({ message: e.detail.value }); },

  async connect() {
    try {
      const s = await this._client.connect({
        url: this.data.url,
        tenantId: this.data.tenantId,
        token: this.data.token,
        deviceId: this.data.deviceId
      });
      appendLog(this, `connect ok user=${s.userId}`);
    } catch (e) {
      appendLog(this, `connect failed: ${e && e.message ? e.message : e}`);
    }
  },

  async send() {
    try {
      const bytes = new TextEncoder().encode(this.data.message);
      const ack = await this._client.sendSingleChat({ toUserId: this.data.toUserId, msgBody: bytes });
      appendLog(this, `send ack status=${ack.status}`);
    } catch (e) {
      appendLog(this, `send failed: ${e && e.message ? e.message : e}`);
    }
  },

  async disconnect() {
    try {
      await this._client.disconnect();
      appendLog(this, "disconnected");
    } catch {}
  }
});

