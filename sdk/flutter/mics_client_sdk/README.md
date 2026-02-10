# mics_client_sdk (Flutter/Dart)

客户端连接 SDK：WebSocket `/ws?tenantId={TenantId}&token={Token}&deviceId={DeviceId}` + Protobuf binary（对齐 `docs/MICS（极简IM通讯服务）需求文档V1.0.md` 4.6.3 / 6.2）。

## Install / Dev

```bash
cd sdk/flutter/mics_client_sdk
dart pub get
dart test
```

## Usage

```dart
import 'dart:typed_data';
import 'package:mics_client_sdk/mics_client_sdk.dart';

final client = MicsClient(MicsClientOptions.defaults());
client.stateStream.listen((s) => print('state=$s'));
client.connectedStream.listen((s) => print('connected user=${s.userId} node=${s.nodeId}'));
client.deliveryStream.listen((d) => print('delivery msgId=${d.message.msgId}'));
client.ackStream.listen((a) => print('ack ${a.msgId} ${a.status}'));

await client.connect(MicsConnectParams(
  url: 'ws://localhost:8080/ws',
  tenantId: 't1',
  token: 'valid:u1',
  deviceId: 'dev1',
));

await client.sendSingleChat(toUserId: 'u2', msgBody: Uint8List.fromList([1,2,3]));
```

## Optional: Message Encryption (AES-GCM)

```dart
final crypto = AesGcmMessageCrypto(Uint8List(32)); // demo key
final client = MicsClient(MicsClientOptions.defaults(messageCrypto: crypto));
```

