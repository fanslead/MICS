import 'crypto/message_crypto.dart';

class MicsClientOptions {
  final Duration connectTimeout;
  final Duration ackTimeout;
  final int maxSendAttempts;
  final Duration heartbeatInterval;
  final bool autoReconnect;
  final Duration reconnectMinDelay;
  final Duration reconnectMaxDelay;
  final MessageCrypto? messageCrypto;

  const MicsClientOptions({
    required this.connectTimeout,
    required this.ackTimeout,
    required this.maxSendAttempts,
    required this.heartbeatInterval,
    required this.autoReconnect,
    required this.reconnectMinDelay,
    required this.reconnectMaxDelay,
    this.messageCrypto,
  });

  factory MicsClientOptions.defaults({
    Duration? connectTimeout,
    Duration? ackTimeout,
    int? maxSendAttempts,
    Duration? heartbeatInterval,
    bool? autoReconnect,
    Duration? reconnectMinDelay,
    Duration? reconnectMaxDelay,
    MessageCrypto? messageCrypto,
  }) {
    return MicsClientOptions(
      connectTimeout: connectTimeout ?? const Duration(seconds: 5),
      ackTimeout: ackTimeout ?? const Duration(seconds: 3),
      maxSendAttempts: maxSendAttempts ?? 3,
      heartbeatInterval: heartbeatInterval ?? const Duration(seconds: 10),
      autoReconnect: autoReconnect ?? true,
      reconnectMinDelay: reconnectMinDelay ?? const Duration(milliseconds: 200),
      reconnectMaxDelay: reconnectMaxDelay ?? const Duration(seconds: 5),
      messageCrypto: messageCrypto,
    );
  }
}

