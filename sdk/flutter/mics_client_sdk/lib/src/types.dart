import 'dart:typed_data';

import 'proto/proto.dart';

enum MicsClientState {
  disconnected,
  connecting,
  connected,
  reconnecting,
  disposing,
}

class MicsSession {
  final String tenantId;
  final String userId;
  final String deviceId;
  final String nodeId;
  final String traceId;

  const MicsSession({
    required this.tenantId,
    required this.userId,
    required this.deviceId,
    required this.nodeId,
    required this.traceId,
  });

  static MicsSession fromConnectAck(ConnectAck ack) => MicsSession(
        tenantId: ack.tenantId,
        userId: ack.userId,
        deviceId: ack.deviceId,
        nodeId: ack.nodeId,
        traceId: ack.traceId,
      );
}

class MicsConnectParams {
  final String url;
  final String tenantId;
  final String token;
  final String deviceId;

  const MicsConnectParams({
    required this.url,
    required this.tenantId,
    required this.token,
    required this.deviceId,
  });
}

class MicsSendSingleChatParams {
  final String toUserId;
  final Uint8List msgBody;
  final String? msgId;

  const MicsSendSingleChatParams({
    required this.toUserId,
    required this.msgBody,
    this.msgId,
  });
}

class MicsSendGroupChatParams {
  final String groupId;
  final Uint8List msgBody;
  final String? msgId;

  const MicsSendGroupChatParams({
    required this.groupId,
    required this.msgBody,
    this.msgId,
  });
}

