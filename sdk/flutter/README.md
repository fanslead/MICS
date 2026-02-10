# MICS Flutter SDKs

当前包含：
- `mics_client_sdk`：客户端连接 SDK（Flutter / Dart），WebSocket + Protobuf binary

## Build / Test

```bash
cd sdk/flutter/mics_client_sdk
dart pub get
dart test
```

## Example (Dart console)

```bash
cd sdk/flutter/mics_client_sdk
dart run example/console.dart --url ws://localhost:8080/ws --tenantId t1 --token valid:u1 --deviceId dev1 --toUserId u2
```

