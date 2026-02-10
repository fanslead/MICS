import { AesGcmMessageCrypto, MicsClient, MicsClientOptions } from "../../src";

const el = document.getElementById("app")!;

el.innerHTML = `
  <div style="font-family: system-ui; max-width: 920px; margin: 24px auto; padding: 0 16px;">
    <h1 style="margin: 0 0 12px;">MICS TypeScript SDK Demo</h1>
    <p style="margin: 0 0 16px; color: #555;">
      Connect to <code>/ws?tenantId&token&deviceId</code>, send single chat, observe ACK/Delivery.
    </p>

    <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 12px;">
      <label>URL<br/><input id="url" style="width: 100%;" value="ws://localhost:8080/ws" /></label>
      <label>TenantId<br/><input id="tenantId" style="width: 100%;" value="t1" /></label>
      <label>Token<br/><input id="token" style="width: 100%;" value="valid:u1" /></label>
      <label>DeviceId<br/><input id="deviceId" style="width: 100%;" value="dev1" /></label>
      <label>ToUserId<br/><input id="toUserId" style="width: 100%;" value="u2" /></label>
      <label>Encrypt (AES-GCM)<br/>
        <select id="encrypt" style="width: 100%;">
          <option value="off" selected>Off</option>
          <option value="on">On (dev key)</option>
        </select>
      </label>
    </div>

    <div style="display:flex; gap: 8px; margin: 12px 0;">
      <button id="connect">Connect</button>
      <button id="send" disabled>Send</button>
      <button id="disconnect" disabled>Disconnect</button>
    </div>

    <label>Payload (hex)<br/><input id="payload" style="width: 100%;" value="010203" /></label>

    <pre id="log" style="margin-top: 12px; padding: 12px; background: #111; color: #0f0; height: 360px; overflow: auto;"></pre>
  </div>
`;

function qs<T extends HTMLElement>(id: string): T {
  const node = document.getElementById(id);
  if (!node) throw new Error("missing element: " + id);
  return node as T;
}

const logEl = qs<HTMLPreElement>("log");
function log(line: string) {
  logEl.textContent += line + "\n";
  logEl.scrollTop = logEl.scrollHeight;
}

let client: MicsClient | null = null;

qs<HTMLButtonElement>("connect").onclick = async () => {
  const url = qs<HTMLInputElement>("url").value;
  const tenantId = qs<HTMLInputElement>("tenantId").value;
  const token = qs<HTMLInputElement>("token").value;
  const deviceId = qs<HTMLInputElement>("deviceId").value;

  const encrypt = qs<HTMLSelectElement>("encrypt").value === "on";
  const crypto = encrypt ? new AesGcmMessageCrypto(new Uint8Array(32)) : undefined;

  client = new MicsClient(
    MicsClientOptions.default({
      heartbeatIntervalMs: 10_000,
      autoReconnect: true,
      messageCrypto: crypto
    })
  );

  client.onStateChanged = (s) => log("state: " + s);
  client.onConnected = (s) => log(`connected tenant=${s.tenantId} user=${s.userId} node=${s.nodeId} trace=${s.traceId}`);
  client.onAckReceived = (a) => log(`ack msgId=${a.msgId} status=${a.status} reason=${a.reason}`);
  client.onDeliveryReceived = (d) => log(`delivery msgId=${d.message?.msgId} from=${d.message?.userId} bytes=${d.message?.msgBody?.byteLength ?? 0}`);

  try {
    await client.connect({ url, tenantId, token, deviceId });
    qs<HTMLButtonElement>("send").disabled = false;
    qs<HTMLButtonElement>("disconnect").disabled = false;
  } catch (e: any) {
    log("connect failed: " + (e?.message ?? String(e)));
  }
};

qs<HTMLButtonElement>("send").onclick = async () => {
  if (!client) return;
  const toUserId = qs<HTMLInputElement>("toUserId").value;
  const payloadHex = qs<HTMLInputElement>("payload").value.trim();
  const bytes = hexToBytes(payloadHex);
  try {
    const ack = await client.sendSingleChat({ toUserId, msgBody: bytes });
    log("send result: " + ack.status + " reason=" + ack.reason);
  } catch (e: any) {
    log("send failed: " + (e?.message ?? String(e)));
  }
};

qs<HTMLButtonElement>("disconnect").onclick = async () => {
  try {
    await client?.disconnect();
  } finally {
    client = null;
    qs<HTMLButtonElement>("send").disabled = true;
    qs<HTMLButtonElement>("disconnect").disabled = true;
  }
};

function hexToBytes(hex: string): Uint8Array {
  const cleaned = hex.replace(/[^0-9a-f]/gi, "");
  if (cleaned.length % 2 !== 0) throw new Error("hex length must be even");
  const out = new Uint8Array(cleaned.length / 2);
  for (let i = 0; i < out.length; i++) {
    out[i] = parseInt(cleaned.slice(i * 2, i * 2 + 2), 16);
  }
  return out;
}

