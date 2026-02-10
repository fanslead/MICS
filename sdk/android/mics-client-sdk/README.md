# mics-client-sdk (Android/Kotlin)

Android(Kotlin) 客户端连接 SDK：OkHttp WebSocket + Protobuf binary（对齐 `docs/MICS（极简IM通讯服务）需求文档V1.0.md` 4.6.3 / 6.2）。

## Build / Test

```bash
cd sdk/android/mics-client-sdk
./gradlew test
```

## Usage

```kotlin
val client = MicsClient(MicsClientOptions())
val session = client.connect(MicsConnectParams(
  url = "ws://localhost:8080/ws",
  tenantId = "t1",
  token = "valid:u1",
  deviceId = "dev1",
))

val ack = client.sendSingleChat(toUserId = "u2", msgBody = "hi".toByteArray())
```

## Optional: Message Encryption (AES-GCM)

密文格式：`[version=1][nonce(12)][tag(16)][ciphertext]`，与 TS/Flutter/小程序 SDK 对齐。

```kotlin
val crypto = AesGcmMessageCrypto(ByteArray(32))
val client = MicsClient(MicsClientOptions(messageCrypto = crypto))
```

