import 'dart:async';
import 'dart:convert';
import 'dart:typed_data';

import 'package:mics_client_sdk/mics_client_sdk.dart';

void main(List<String> args) async {
  final map = _parseArgs(args);

  final url = map['url'] ?? 'ws://localhost:8080/ws';
  final tenantId = map['tenantId'] ?? 't1';
  final token = map['token'] ?? 'valid:u1';
  final deviceId = map['deviceId'] ?? 'dev1';
  final toUserId = map['toUserId'] ?? 'u2';

  final client = MicsClient(MicsClientOptions.defaults());
  client.stateStream.listen((s) => print('state=$s'));
  client.connectedStream.listen((s) => print('connected tenant=${s.tenantId} user=${s.userId} node=${s.nodeId} traceId=${s.traceId}'));
  client.deliveryStream.listen((d) => print('delivery msgId=${d.message.msgId} from=${d.message.userId}'));
  client.ackStream.listen((a) => print('ack msgId=${a.msgId} status=${a.status} reason=${a.reason}'));
  client.serverErrorStream.listen((e) => print('server_error code=${e.code} message=${e.message}'));

  final session = await client.connect(MicsConnectParams(url: url, tenantId: tenantId, token: token, deviceId: deviceId));
  print('session user=${session.userId} device=${session.deviceId}');

  final body = utf8.encode('hello from dart ${DateTime.now().toIso8601String()}');
  final ack = await client.sendSingleChat(toUserId: toUserId, msgBody: Uint8List.fromList(body));
  print('send result: ${ack.status}');

  await Future<void>.delayed(const Duration(seconds: 2));
  await client.disconnect();
}

Map<String, String> _parseArgs(List<String> args) {
  final out = <String, String>{};
  for (int i = 0; i < args.length; i++) {
    final a = args[i];
    if (!a.startsWith('--')) continue;
    final key = a.substring(2);
    if (i + 1 >= args.length) break;
    out[key] = args[i + 1];
    i++;
  }
  return out;
}

