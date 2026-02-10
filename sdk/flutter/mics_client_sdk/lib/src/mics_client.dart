import 'dart:async';
import 'dart:math';
import 'dart:typed_data';

import 'package:fixnum/fixnum.dart';
import 'package:web_socket_channel/web_socket_channel.dart';

import 'crypto/message_crypto.dart';
import 'options.dart';
import 'proto/proto.dart';
import 'types.dart';
import 'ws_uri_builder.dart';

class MicsClient {
  final MicsClientOptions options;

  MicsClient(this.options);

  final _stateController = StreamController<MicsClientState>.broadcast();
  final _connectedController = StreamController<MicsSession>.broadcast();
  final _deliveryController = StreamController<MessageDelivery>.broadcast();
  final _ackController = StreamController<MessageAck>.broadcast();
  final _serverErrorController = StreamController<ServerError>.broadcast();

  Stream<MicsClientState> get stateStream => _stateController.stream;
  Stream<MicsSession> get connectedStream => _connectedController.stream;
  Stream<MessageDelivery> get deliveryStream => _deliveryController.stream;
  Stream<MessageAck> get ackStream => _ackController.stream;
  Stream<ServerError> get serverErrorStream => _serverErrorController.stream;

  MicsClientState _state = MicsClientState.disconnected;
  MicsClientState get state => _state;

  WebSocketChannel? _ws;
  StreamSubscription? _wsSub;

  MicsSession? _session;
  MicsConnectParams? _connectParams;

  Timer? _heartbeatTimer;

  Completer<MicsSession>? _connectAckCompleter;
  final List<Completer<void>> _connectedWaiters = [];

  final Map<String, Completer<MessageAck>> _pendingAcks = {};
  int _nextMsgId = 0;

  Future<void>? _reconnectFuture;

  Future<MicsSession> connect(MicsConnectParams params) async {
    if (_state != MicsClientState.disconnected) {
      throw StateError('client is not disconnected');
    }

    _connectParams = params;
    _setState(MicsClientState.connecting);
    final session = await _connectInternal(params);
    return session;
  }

  Future<void> disconnect() async {
    _setState(MicsClientState.disposing);
    _stopHeartbeat();
    await _closeWs();
    _session = null;
    _setState(MicsClientState.disconnected);
  }

  Future<MessageAck> sendSingleChat({
    required String toUserId,
    required Uint8List msgBody,
    String? msgId,
  }) async {
    final id = (msgId != null && msgId.isNotEmpty) ? msgId : _nextId();
    final pair = await _waitConnectedPair();

    final body = await _prepareOutboundBody(msgBody);
    final msg = MessageRequest(
      tenantId: pair.session.tenantId,
      userId: pair.session.userId,
      deviceId: pair.session.deviceId,
      msgId: id,
      msgType: MessageType.SINGLE_CHAT,
      toUserId: toUserId,
      groupId: '',
      msgBody: body,
      timestampMs: Int64(_nowMs()),
    );

    return _sendWithRetry(msg);
  }

  Future<MessageAck> sendGroupChat({
    required String groupId,
    required Uint8List msgBody,
    String? msgId,
  }) async {
    final id = (msgId != null && msgId.isNotEmpty) ? msgId : _nextId();
    final pair = await _waitConnectedPair();

    final body = await _prepareOutboundBody(msgBody);
    final msg = MessageRequest(
      tenantId: pair.session.tenantId,
      userId: pair.session.userId,
      deviceId: pair.session.deviceId,
      msgId: id,
      msgType: MessageType.GROUP_CHAT,
      toUserId: '',
      groupId: groupId,
      msgBody: body,
      timestampMs: Int64(_nowMs()),
    );

    return _sendWithRetry(msg);
  }

  Future<MessageAck> _sendWithRetry(MessageRequest msg) async {
    final ackCompleter = Completer<MessageAck>();
    _pendingAcks[msg.msgId] = ackCompleter;

    final maxAttempts = max(1, options.maxSendAttempts);
    int sentAttempts = 0;

    try {
      while (sentAttempts < maxAttempts) {
        if (ackCompleter.isCompleted) {
          return ackCompleter.future;
        }

        final pair = await _waitConnectedPair();
        msg.timestampMs = Int64(_nowMs());

        try {
          final frame = ClientFrame(message: msg);
          pair.ws.sink.add(Uint8List.fromList(frame.writeToBuffer()));
          sentAttempts++;
        } catch (_) {
          _tryStartReconnect();
          continue;
        }

        final result = await Future.any([
          ackCompleter.future.then((ack) => ack),
          Future<MessageAck?>.delayed(options.ackTimeout, () => null),
        ]);

        if (result != null) {
          return result;
        }
      }

      return MessageAck(
        msgId: msg.msgId,
        status: AckStatus.FAILED,
        timestampMs: Int64(_nowMs()),
        reason: 'ack timeout',
      );
    } finally {
      _pendingAcks.remove(msg.msgId);
    }
  }

  Future<_ConnectedPair> _waitConnectedPair() async {
    while (true) {
      final ws = _ws;
      final session = _session;
      if (_state == MicsClientState.connected && ws != null && session != null) {
        return _ConnectedPair(ws: ws, session: session);
      }

      if (_state == MicsClientState.disconnected || _state == MicsClientState.disposing) {
        throw StateError('client is not connected');
      }

      if (!options.autoReconnect) {
        throw StateError('client is not connected');
      }

      final waiter = Completer<void>();
      _connectedWaiters.add(waiter);
      await waiter.future;
    }
  }

  void _signalConnected() {
    final waiters = List<Completer<void>>.from(_connectedWaiters);
    _connectedWaiters.clear();
    for (final w in waiters) {
      if (!w.isCompleted) w.complete();
    }
  }

  Future<MicsSession> _connectInternal(MicsConnectParams params) async {
    await _closeWs();
    _session = null;

    final connectAck = Completer<MicsSession>();
    _connectAckCompleter = connectAck;

    final uri = buildWsUri(params.url, params.tenantId, params.token, params.deviceId);

    late final WebSocketChannel channel;
    try {
      channel = WebSocketChannel.connect(uri);
    } catch (e) {
      _connectAckCompleter = null;
      rethrow;
    }

    _ws = channel;
    _wsSub = channel.stream.listen(
      (data) => _handleWsMessage(data),
      onError: (_) {
        // rely on onDone for state changes
      },
      onDone: _handleWsDone,
      cancelOnError: false,
    );

    final session = await Future.any([
      connectAck.future,
      Future<MicsSession>.delayed(options.connectTimeout, () => throw TimeoutException('connect timeout')),
    ]);

    _session = session;
    _setState(MicsClientState.connected);
    _startHeartbeat();
    _connectedController.add(session);
    _signalConnected();
    return session;
  }

  Future<void> _handleWsMessage(dynamic data) async {
    final bytes = _toBytes(data);
    if (bytes.isEmpty) return;

    ServerFrame frame;
    try {
      frame = ServerFrame.fromBuffer(bytes);
    } catch (_) {
      return;
    }

    switch (frame.whichPayload()) {
      case ServerFrame_Payload.connectAck:
        final ack = frame.connectAck;
        if (ack.code != 1000) {
          _connectAckCompleter?.completeError(StateError('connect rejected: ${ack.code}'));
          return;
        }
        _connectAckCompleter?.complete(MicsSession.fromConnectAck(ack));
        return;

      case ServerFrame_Payload.ack:
        final ack = frame.ack;
        final c = _pendingAcks[ack.msgId];
        if (c != null && !c.isCompleted) {
          c.complete(ack);
        }
        _ackController.add(ack);
        return;

      case ServerFrame_Payload.delivery:
        var delivery = frame.delivery;
        final crypto = options.messageCrypto;
        if (crypto != null && delivery.hasMessage() && delivery.message.msgBody.isNotEmpty) {
          try {
            final dec = await crypto.decrypt(Uint8List.fromList(delivery.message.msgBody));
            final msg = delivery.message.deepCopy()..msgBody = dec;
            delivery = MessageDelivery(message: msg);
          } catch (_) {
            // best-effort: surface ciphertext if decrypt fails
          }
        }
        _deliveryController.add(delivery);
        return;

      case ServerFrame_Payload.error:
        _serverErrorController.add(frame.error);
        return;

      case ServerFrame_Payload.heartbeatPong:
      case ServerFrame_Payload.notSet:
        return;
    }
  }

  void _handleWsDone() {
    _stopHeartbeat();
    _ws = null;
    _wsSub?.cancel();
    _wsSub = null;
    _session = null;

    if (_state == MicsClientState.disposing) return;
    _tryStartReconnect();
  }

  void _tryStartReconnect() {
    if (!options.autoReconnect) {
      _setState(MicsClientState.disconnected);
      return;
    }
    if (_reconnectFuture != null) return;
    final params = _connectParams;
    if (params == null) {
      _setState(MicsClientState.disconnected);
      return;
    }

    _setState(MicsClientState.reconnecting);

    _reconnectFuture = () async {
      var delay = options.reconnectMinDelay;
      final maxDelay = options.reconnectMaxDelay;

      while (true) {
        try {
          _connectAckCompleter = null;
          await _connectInternal(params);
          return;
        } catch (_) {
          final ms = delay.inMilliseconds;
          if (ms > 0) {
            await Future.delayed(_withJitter(delay));
          }
          final nextMs = ms == 0 ? 50 : ms * 2;
          delay = Duration(milliseconds: min(maxDelay.inMilliseconds, nextMs));
        }
      }
    }().whenComplete(() {
      _reconnectFuture = null;
    });
  }

  Duration _withJitter(Duration d) {
    if (d.inMilliseconds <= 0) return d;
    final r = Random().nextInt(max(1, d.inMilliseconds ~/ 4));
    return Duration(milliseconds: d.inMilliseconds + r);
  }

  void _startHeartbeat() {
    _stopHeartbeat();
    if (options.heartbeatInterval.inMilliseconds <= 0) return;

    _heartbeatTimer = Timer.periodic(options.heartbeatInterval, (_) {
      final ws = _ws;
      if (ws == null) return;
      try {
        final ping = HeartbeatPing(timestampMs: Int64(_nowMs()));
        final frame = ClientFrame(heartbeatPing: ping);
        ws.sink.add(Uint8List.fromList(frame.writeToBuffer()));
      } catch (_) {
        // ignore
      }
    });
  }

  void _stopHeartbeat() {
    _heartbeatTimer?.cancel();
    _heartbeatTimer = null;
  }

  Future<void> _closeWs() async {
    final ws = _ws;
    _ws = null;
    _session = null;
    _connectAckCompleter = null;

    await _wsSub?.cancel();
    _wsSub = null;

    if (ws != null) {
      try {
        await ws.sink.close(1000, 'dispose');
      } catch (_) {
        // ignore
      }
    }
  }

  void _setState(MicsClientState s) {
    if (_state == s) return;
    _state = s;
    _stateController.add(s);
  }

  String _nextId() {
    _nextMsgId++;
    return _nextMsgId.toString();
  }

  Future<Uint8List> _prepareOutboundBody(Uint8List plaintext) async {
    if (plaintext.isEmpty) return Uint8List(0);
    final MessageCrypto? crypto = options.messageCrypto;
    if (crypto == null) return plaintext;
    return crypto.encrypt(plaintext);
  }

  static int _nowMs() => DateTime.now().millisecondsSinceEpoch;

  static Uint8List _toBytes(dynamic data) {
    if (data is Uint8List) return data;
    if (data is List<int>) return Uint8List.fromList(data);
    return Uint8List(0);
  }
}

class _ConnectedPair {
  final WebSocketChannel ws;
  final MicsSession session;

  const _ConnectedPair({required this.ws, required this.session});
}

