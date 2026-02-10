# MICS Android SDKs

当前包含：
- `mics-client-sdk`：Android(Kotlin) 客户端连接 SDK（OkHttp WebSocket + Protobuf binary）

## Build / Test

```bash
cd sdk/android/mics-client-sdk
.\gradlew.bat test
```

## Sample (console)

```bash
cd sdk/android/mics-client-sdk
.\gradlew.bat :samples:console:run --args="--url ws://localhost:8080/ws --tenantId t1 --token valid:u1 --deviceId dev1 --toUserId u2"
```

