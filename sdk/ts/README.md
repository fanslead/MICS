# MICS TypeScript SDK (Web/H5)

客户端连接 SDK（浏览器）：WebSocket + Protobuf binary。

## Install / Dev

在仓库内（示例/联调用途）：

```bash
cd sdk/ts
npm install
npm test
npm run dev
```

打开 `http://localhost:5173`，填写网关地址与租户参数后连接发送。

## Usage

```ts
import { MicsClient, MicsClientOptions } from "@mics/sdk-ts";

const client = new MicsClient(MicsClientOptions.default({ heartbeatIntervalMs: 10_000, autoReconnect: true }));
client.onConnected = (s) => console.log("connected", s);
client.onDeliveryReceived = (d) => console.log("delivery", d.message?.msgId);
client.onAckReceived = (a) => console.log("ack", a.msgId, a.status);

await client.connect({ url: "ws://localhost:8080/ws", tenantId: "t1", token: "valid:u1", deviceId: "dev1" });
await client.sendSingleChat({ toUserId: "u2", msgBody: new Uint8Array([1, 2, 3]) });
```

## Optional: Message Encryption (AES-GCM)

```ts
import { AesGcmMessageCrypto, MicsClient, MicsClientOptions } from "@mics/sdk-ts";

const crypto = new AesGcmMessageCrypto(new Uint8Array(32)); // demo key
const client = new MicsClient(MicsClientOptions.default({ messageCrypto: crypto }));
```

