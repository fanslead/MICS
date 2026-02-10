# @mics/wechat-mp-sdk

微信小程序客户端连接 SDK：WebSocket `/ws?tenantId={TenantId}&token={Token}&deviceId={DeviceId}` + Protobuf binary。

## Install / Build

```bash
cd sdk/wechat/mics-client-sdk
npm install
npm test
npm run build
```

## Usage (Mini Program)

> 说明：小程序侧可通过「构建 npm」将本 SDK 引入（或将 `dist/` 直接拷贝到小程序工程中引用）。

```js
const { MicsClient, MicsClientOptions } = require("@mics/wechat-mp-sdk");

const client = new MicsClient(MicsClientOptions.default());
client.onConnected = (s) => console.log("connected", s.userId, s.nodeId);
client.onDeliveryReceived = (d) => console.log("delivery", d.message?.msgId);
client.onAckReceived = (a) => console.log("ack", a.msgId, a.status);

client.connect({ url: "wss://example.com/ws", tenantId: "t1", token: "valid:u1", deviceId: "dev1" });
```

## Optional: Message Encryption (AES-GCM)

```js
const { AesGcmMessageCrypto, MicsClient, MicsClientOptions } = require("@mics/wechat-mp-sdk");
const crypto = new AesGcmMessageCrypto(new Uint8Array(32));
const client = new MicsClient(MicsClientOptions.default({ messageCrypto: crypto }));
```

