# mics-hook-sdk (Java)

服务端 Hook SDK：用于业务方实现 MICS 的同步 HTTP Hook（`/auth`、`/check-message`、`/get-group-members`、`/get-offline-messages`）以及消费 MQ Hook（Kafka）事件时的 Protobuf 解析与验签。

## HTTP Hook 签名/验签

Gateway 侧签名算法（Base64）：
- HMAC-SHA256(key = `tenant_secret`)
- 输入拼接：`Serialize(payloadWithMetaSignCleared)` + `RequestId(UTF8)` + `TimestampMs(int64 little-endian)`

SDK 提供：
- `com.mics.hooksdk.HookSigner.computeBase64(...)`
- `com.mics.hooksdk.HookSigner.verify(...)`

## MQ 事件签名/验签

Gateway 侧签名算法（Base64）：
- HMAC-SHA256(key = `tenant_secret`)
- 输入：`Serialize(mqEventWithSignCleared)`

SDK 提供：
- `com.mics.hooksdk.MqEventSigner.computeBase64(...)`
- `com.mics.hooksdk.MqEventSigner.verify(...)`
- `com.mics.hooksdk.MqEventDecoder.tryDecodeConnectAck(...)` / `tryDecodeMessage(...)`

## Minimal HTTP Server（JDK 内置）

SDK 内置一个轻量服务器封装，业务方只需实现 `MicsHookHandler`：

```java
var options = new MicsHookServerOptions(tenantId -> "secret", true);
var server = new MicsHookHttpServer(new InetSocketAddress(8091), handler, options);
server.start();
```

一键示例：`sdk/java/samples/hook-server`
