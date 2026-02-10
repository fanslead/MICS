// This is a generated file - do not edit.
//
// Generated from Protos/mics_message.proto.

// @dart = 3.3

// ignore_for_file: annotate_overrides, camel_case_types, comment_references
// ignore_for_file: constant_identifier_names
// ignore_for_file: curly_braces_in_flow_control_structures
// ignore_for_file: deprecated_member_use_from_same_package, library_prefixes
// ignore_for_file: non_constant_identifier_names, prefer_relative_imports

import 'dart:core' as $core;

import 'package:fixnum/fixnum.dart' as $fixnum;
import 'package:protobuf/protobuf.dart' as $pb;

import 'mics_message.pbenum.dart';

export 'package:protobuf/protobuf.dart' show GeneratedMessageGenericExtensions;

export 'mics_message.pbenum.dart';

class MessageRequest extends $pb.GeneratedMessage {
  factory MessageRequest({
    $core.String? tenantId,
    $core.String? userId,
    $core.String? deviceId,
    $core.String? msgId,
    MessageType? msgType,
    $core.String? toUserId,
    $core.String? groupId,
    $core.List<$core.int>? msgBody,
    $fixnum.Int64? timestampMs,
  }) {
    final result = create();
    if (tenantId != null) result.tenantId = tenantId;
    if (userId != null) result.userId = userId;
    if (deviceId != null) result.deviceId = deviceId;
    if (msgId != null) result.msgId = msgId;
    if (msgType != null) result.msgType = msgType;
    if (toUserId != null) result.toUserId = toUserId;
    if (groupId != null) result.groupId = groupId;
    if (msgBody != null) result.msgBody = msgBody;
    if (timestampMs != null) result.timestampMs = timestampMs;
    return result;
  }

  MessageRequest._();

  factory MessageRequest.fromBuffer($core.List<$core.int> data,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromBuffer(data, registry);
  factory MessageRequest.fromJson($core.String json,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
      _omitMessageNames ? '' : 'MessageRequest',
      package:
          const $pb.PackageName(_omitMessageNames ? '' : 'mics.message.v1'),
      createEmptyInstance: create)
    ..aOS(1, _omitFieldNames ? '' : 'tenantId')
    ..aOS(2, _omitFieldNames ? '' : 'userId')
    ..aOS(3, _omitFieldNames ? '' : 'deviceId')
    ..aOS(4, _omitFieldNames ? '' : 'msgId')
    ..aE<MessageType>(5, _omitFieldNames ? '' : 'msgType',
        enumValues: MessageType.values)
    ..aOS(6, _omitFieldNames ? '' : 'toUserId')
    ..aOS(7, _omitFieldNames ? '' : 'groupId')
    ..a<$core.List<$core.int>>(
        8, _omitFieldNames ? '' : 'msgBody', $pb.PbFieldType.OY)
    ..aInt64(9, _omitFieldNames ? '' : 'timestampMs')
    ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  MessageRequest clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  MessageRequest copyWith(void Function(MessageRequest) updates) =>
      super.copyWith((message) => updates(message as MessageRequest))
          as MessageRequest;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static MessageRequest create() => MessageRequest._();
  @$core.override
  MessageRequest createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static MessageRequest getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<MessageRequest>(create);
  static MessageRequest? _defaultInstance;

  @$pb.TagNumber(1)
  $core.String get tenantId => $_getSZ(0);
  @$pb.TagNumber(1)
  set tenantId($core.String value) => $_setString(0, value);
  @$pb.TagNumber(1)
  $core.bool hasTenantId() => $_has(0);
  @$pb.TagNumber(1)
  void clearTenantId() => $_clearField(1);

  @$pb.TagNumber(2)
  $core.String get userId => $_getSZ(1);
  @$pb.TagNumber(2)
  set userId($core.String value) => $_setString(1, value);
  @$pb.TagNumber(2)
  $core.bool hasUserId() => $_has(1);
  @$pb.TagNumber(2)
  void clearUserId() => $_clearField(2);

  @$pb.TagNumber(3)
  $core.String get deviceId => $_getSZ(2);
  @$pb.TagNumber(3)
  set deviceId($core.String value) => $_setString(2, value);
  @$pb.TagNumber(3)
  $core.bool hasDeviceId() => $_has(2);
  @$pb.TagNumber(3)
  void clearDeviceId() => $_clearField(3);

  @$pb.TagNumber(4)
  $core.String get msgId => $_getSZ(3);
  @$pb.TagNumber(4)
  set msgId($core.String value) => $_setString(3, value);
  @$pb.TagNumber(4)
  $core.bool hasMsgId() => $_has(3);
  @$pb.TagNumber(4)
  void clearMsgId() => $_clearField(4);

  @$pb.TagNumber(5)
  MessageType get msgType => $_getN(4);
  @$pb.TagNumber(5)
  set msgType(MessageType value) => $_setField(5, value);
  @$pb.TagNumber(5)
  $core.bool hasMsgType() => $_has(4);
  @$pb.TagNumber(5)
  void clearMsgType() => $_clearField(5);

  @$pb.TagNumber(6)
  $core.String get toUserId => $_getSZ(5);
  @$pb.TagNumber(6)
  set toUserId($core.String value) => $_setString(5, value);
  @$pb.TagNumber(6)
  $core.bool hasToUserId() => $_has(5);
  @$pb.TagNumber(6)
  void clearToUserId() => $_clearField(6);

  @$pb.TagNumber(7)
  $core.String get groupId => $_getSZ(6);
  @$pb.TagNumber(7)
  set groupId($core.String value) => $_setString(6, value);
  @$pb.TagNumber(7)
  $core.bool hasGroupId() => $_has(6);
  @$pb.TagNumber(7)
  void clearGroupId() => $_clearField(7);

  @$pb.TagNumber(8)
  $core.List<$core.int> get msgBody => $_getN(7);
  @$pb.TagNumber(8)
  set msgBody($core.List<$core.int> value) => $_setBytes(7, value);
  @$pb.TagNumber(8)
  $core.bool hasMsgBody() => $_has(7);
  @$pb.TagNumber(8)
  void clearMsgBody() => $_clearField(8);

  @$pb.TagNumber(9)
  $fixnum.Int64 get timestampMs => $_getI64(8);
  @$pb.TagNumber(9)
  set timestampMs($fixnum.Int64 value) => $_setInt64(8, value);
  @$pb.TagNumber(9)
  $core.bool hasTimestampMs() => $_has(8);
  @$pb.TagNumber(9)
  void clearTimestampMs() => $_clearField(9);
}

class MessageAck extends $pb.GeneratedMessage {
  factory MessageAck({
    $core.String? msgId,
    AckStatus? status,
    $fixnum.Int64? timestampMs,
    $core.String? reason,
  }) {
    final result = create();
    if (msgId != null) result.msgId = msgId;
    if (status != null) result.status = status;
    if (timestampMs != null) result.timestampMs = timestampMs;
    if (reason != null) result.reason = reason;
    return result;
  }

  MessageAck._();

  factory MessageAck.fromBuffer($core.List<$core.int> data,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromBuffer(data, registry);
  factory MessageAck.fromJson($core.String json,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
      _omitMessageNames ? '' : 'MessageAck',
      package:
          const $pb.PackageName(_omitMessageNames ? '' : 'mics.message.v1'),
      createEmptyInstance: create)
    ..aOS(1, _omitFieldNames ? '' : 'msgId')
    ..aE<AckStatus>(2, _omitFieldNames ? '' : 'status',
        enumValues: AckStatus.values)
    ..aInt64(3, _omitFieldNames ? '' : 'timestampMs')
    ..aOS(4, _omitFieldNames ? '' : 'reason')
    ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  MessageAck clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  MessageAck copyWith(void Function(MessageAck) updates) =>
      super.copyWith((message) => updates(message as MessageAck)) as MessageAck;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static MessageAck create() => MessageAck._();
  @$core.override
  MessageAck createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static MessageAck getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<MessageAck>(create);
  static MessageAck? _defaultInstance;

  @$pb.TagNumber(1)
  $core.String get msgId => $_getSZ(0);
  @$pb.TagNumber(1)
  set msgId($core.String value) => $_setString(0, value);
  @$pb.TagNumber(1)
  $core.bool hasMsgId() => $_has(0);
  @$pb.TagNumber(1)
  void clearMsgId() => $_clearField(1);

  @$pb.TagNumber(2)
  AckStatus get status => $_getN(1);
  @$pb.TagNumber(2)
  set status(AckStatus value) => $_setField(2, value);
  @$pb.TagNumber(2)
  $core.bool hasStatus() => $_has(1);
  @$pb.TagNumber(2)
  void clearStatus() => $_clearField(2);

  @$pb.TagNumber(3)
  $fixnum.Int64 get timestampMs => $_getI64(2);
  @$pb.TagNumber(3)
  set timestampMs($fixnum.Int64 value) => $_setInt64(2, value);
  @$pb.TagNumber(3)
  $core.bool hasTimestampMs() => $_has(2);
  @$pb.TagNumber(3)
  void clearTimestampMs() => $_clearField(3);

  @$pb.TagNumber(4)
  $core.String get reason => $_getSZ(3);
  @$pb.TagNumber(4)
  set reason($core.String value) => $_setString(3, value);
  @$pb.TagNumber(4)
  $core.bool hasReason() => $_has(3);
  @$pb.TagNumber(4)
  void clearReason() => $_clearField(4);
}

class ConnectAck extends $pb.GeneratedMessage {
  factory ConnectAck({
    $core.int? code,
    $core.String? tenantId,
    $core.String? userId,
    $core.String? deviceId,
    $core.String? nodeId,
    $core.String? traceId,
  }) {
    final result = create();
    if (code != null) result.code = code;
    if (tenantId != null) result.tenantId = tenantId;
    if (userId != null) result.userId = userId;
    if (deviceId != null) result.deviceId = deviceId;
    if (nodeId != null) result.nodeId = nodeId;
    if (traceId != null) result.traceId = traceId;
    return result;
  }

  ConnectAck._();

  factory ConnectAck.fromBuffer($core.List<$core.int> data,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromBuffer(data, registry);
  factory ConnectAck.fromJson($core.String json,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
      _omitMessageNames ? '' : 'ConnectAck',
      package:
          const $pb.PackageName(_omitMessageNames ? '' : 'mics.message.v1'),
      createEmptyInstance: create)
    ..aI(1, _omitFieldNames ? '' : 'code')
    ..aOS(2, _omitFieldNames ? '' : 'tenantId')
    ..aOS(3, _omitFieldNames ? '' : 'userId')
    ..aOS(4, _omitFieldNames ? '' : 'deviceId')
    ..aOS(5, _omitFieldNames ? '' : 'nodeId')
    ..aOS(6, _omitFieldNames ? '' : 'traceId')
    ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  ConnectAck clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  ConnectAck copyWith(void Function(ConnectAck) updates) =>
      super.copyWith((message) => updates(message as ConnectAck)) as ConnectAck;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static ConnectAck create() => ConnectAck._();
  @$core.override
  ConnectAck createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static ConnectAck getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<ConnectAck>(create);
  static ConnectAck? _defaultInstance;

  @$pb.TagNumber(1)
  $core.int get code => $_getIZ(0);
  @$pb.TagNumber(1)
  set code($core.int value) => $_setSignedInt32(0, value);
  @$pb.TagNumber(1)
  $core.bool hasCode() => $_has(0);
  @$pb.TagNumber(1)
  void clearCode() => $_clearField(1);

  @$pb.TagNumber(2)
  $core.String get tenantId => $_getSZ(1);
  @$pb.TagNumber(2)
  set tenantId($core.String value) => $_setString(1, value);
  @$pb.TagNumber(2)
  $core.bool hasTenantId() => $_has(1);
  @$pb.TagNumber(2)
  void clearTenantId() => $_clearField(2);

  @$pb.TagNumber(3)
  $core.String get userId => $_getSZ(2);
  @$pb.TagNumber(3)
  set userId($core.String value) => $_setString(2, value);
  @$pb.TagNumber(3)
  $core.bool hasUserId() => $_has(2);
  @$pb.TagNumber(3)
  void clearUserId() => $_clearField(3);

  @$pb.TagNumber(4)
  $core.String get deviceId => $_getSZ(3);
  @$pb.TagNumber(4)
  set deviceId($core.String value) => $_setString(3, value);
  @$pb.TagNumber(4)
  $core.bool hasDeviceId() => $_has(3);
  @$pb.TagNumber(4)
  void clearDeviceId() => $_clearField(4);

  @$pb.TagNumber(5)
  $core.String get nodeId => $_getSZ(4);
  @$pb.TagNumber(5)
  set nodeId($core.String value) => $_setString(4, value);
  @$pb.TagNumber(5)
  $core.bool hasNodeId() => $_has(4);
  @$pb.TagNumber(5)
  void clearNodeId() => $_clearField(5);

  @$pb.TagNumber(6)
  $core.String get traceId => $_getSZ(5);
  @$pb.TagNumber(6)
  set traceId($core.String value) => $_setString(5, value);
  @$pb.TagNumber(6)
  $core.bool hasTraceId() => $_has(5);
  @$pb.TagNumber(6)
  void clearTraceId() => $_clearField(6);
}

class MessageDelivery extends $pb.GeneratedMessage {
  factory MessageDelivery({
    MessageRequest? message,
  }) {
    final result = create();
    if (message != null) result.message = message;
    return result;
  }

  MessageDelivery._();

  factory MessageDelivery.fromBuffer($core.List<$core.int> data,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromBuffer(data, registry);
  factory MessageDelivery.fromJson($core.String json,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
      _omitMessageNames ? '' : 'MessageDelivery',
      package:
          const $pb.PackageName(_omitMessageNames ? '' : 'mics.message.v1'),
      createEmptyInstance: create)
    ..aOM<MessageRequest>(1, _omitFieldNames ? '' : 'message',
        subBuilder: MessageRequest.create)
    ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  MessageDelivery clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  MessageDelivery copyWith(void Function(MessageDelivery) updates) =>
      super.copyWith((message) => updates(message as MessageDelivery))
          as MessageDelivery;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static MessageDelivery create() => MessageDelivery._();
  @$core.override
  MessageDelivery createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static MessageDelivery getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<MessageDelivery>(create);
  static MessageDelivery? _defaultInstance;

  @$pb.TagNumber(1)
  MessageRequest get message => $_getN(0);
  @$pb.TagNumber(1)
  set message(MessageRequest value) => $_setField(1, value);
  @$pb.TagNumber(1)
  $core.bool hasMessage() => $_has(0);
  @$pb.TagNumber(1)
  void clearMessage() => $_clearField(1);
  @$pb.TagNumber(1)
  MessageRequest ensureMessage() => $_ensure(0);
}

class ServerError extends $pb.GeneratedMessage {
  factory ServerError({
    $core.int? code,
    $core.String? message,
  }) {
    final result = create();
    if (code != null) result.code = code;
    if (message != null) result.message = message;
    return result;
  }

  ServerError._();

  factory ServerError.fromBuffer($core.List<$core.int> data,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromBuffer(data, registry);
  factory ServerError.fromJson($core.String json,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
      _omitMessageNames ? '' : 'ServerError',
      package:
          const $pb.PackageName(_omitMessageNames ? '' : 'mics.message.v1'),
      createEmptyInstance: create)
    ..aI(1, _omitFieldNames ? '' : 'code')
    ..aOS(2, _omitFieldNames ? '' : 'message')
    ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  ServerError clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  ServerError copyWith(void Function(ServerError) updates) =>
      super.copyWith((message) => updates(message as ServerError))
          as ServerError;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static ServerError create() => ServerError._();
  @$core.override
  ServerError createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static ServerError getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<ServerError>(create);
  static ServerError? _defaultInstance;

  @$pb.TagNumber(1)
  $core.int get code => $_getIZ(0);
  @$pb.TagNumber(1)
  set code($core.int value) => $_setSignedInt32(0, value);
  @$pb.TagNumber(1)
  $core.bool hasCode() => $_has(0);
  @$pb.TagNumber(1)
  void clearCode() => $_clearField(1);

  @$pb.TagNumber(2)
  $core.String get message => $_getSZ(1);
  @$pb.TagNumber(2)
  set message($core.String value) => $_setString(1, value);
  @$pb.TagNumber(2)
  $core.bool hasMessage() => $_has(1);
  @$pb.TagNumber(2)
  void clearMessage() => $_clearField(2);
}

enum ClientFrame_Payload { message, heartbeatPing, notSet }

class ClientFrame extends $pb.GeneratedMessage {
  factory ClientFrame({
    MessageRequest? message,
    HeartbeatPing? heartbeatPing,
  }) {
    final result = create();
    if (message != null) result.message = message;
    if (heartbeatPing != null) result.heartbeatPing = heartbeatPing;
    return result;
  }

  ClientFrame._();

  factory ClientFrame.fromBuffer($core.List<$core.int> data,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromBuffer(data, registry);
  factory ClientFrame.fromJson($core.String json,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromJson(json, registry);

  static const $core.Map<$core.int, ClientFrame_Payload>
      _ClientFrame_PayloadByTag = {
    1: ClientFrame_Payload.message,
    2: ClientFrame_Payload.heartbeatPing,
    0: ClientFrame_Payload.notSet
  };
  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
      _omitMessageNames ? '' : 'ClientFrame',
      package:
          const $pb.PackageName(_omitMessageNames ? '' : 'mics.message.v1'),
      createEmptyInstance: create)
    ..oo(0, [1, 2])
    ..aOM<MessageRequest>(1, _omitFieldNames ? '' : 'message',
        subBuilder: MessageRequest.create)
    ..aOM<HeartbeatPing>(2, _omitFieldNames ? '' : 'heartbeatPing',
        subBuilder: HeartbeatPing.create)
    ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  ClientFrame clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  ClientFrame copyWith(void Function(ClientFrame) updates) =>
      super.copyWith((message) => updates(message as ClientFrame))
          as ClientFrame;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static ClientFrame create() => ClientFrame._();
  @$core.override
  ClientFrame createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static ClientFrame getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<ClientFrame>(create);
  static ClientFrame? _defaultInstance;

  @$pb.TagNumber(1)
  @$pb.TagNumber(2)
  ClientFrame_Payload whichPayload() =>
      _ClientFrame_PayloadByTag[$_whichOneof(0)]!;
  @$pb.TagNumber(1)
  @$pb.TagNumber(2)
  void clearPayload() => $_clearField($_whichOneof(0));

  @$pb.TagNumber(1)
  MessageRequest get message => $_getN(0);
  @$pb.TagNumber(1)
  set message(MessageRequest value) => $_setField(1, value);
  @$pb.TagNumber(1)
  $core.bool hasMessage() => $_has(0);
  @$pb.TagNumber(1)
  void clearMessage() => $_clearField(1);
  @$pb.TagNumber(1)
  MessageRequest ensureMessage() => $_ensure(0);

  @$pb.TagNumber(2)
  HeartbeatPing get heartbeatPing => $_getN(1);
  @$pb.TagNumber(2)
  set heartbeatPing(HeartbeatPing value) => $_setField(2, value);
  @$pb.TagNumber(2)
  $core.bool hasHeartbeatPing() => $_has(1);
  @$pb.TagNumber(2)
  void clearHeartbeatPing() => $_clearField(2);
  @$pb.TagNumber(2)
  HeartbeatPing ensureHeartbeatPing() => $_ensure(1);
}

enum ServerFrame_Payload {
  connectAck,
  delivery,
  ack,
  error,
  heartbeatPong,
  notSet
}

class ServerFrame extends $pb.GeneratedMessage {
  factory ServerFrame({
    ConnectAck? connectAck,
    MessageDelivery? delivery,
    MessageAck? ack,
    ServerError? error,
    HeartbeatPong? heartbeatPong,
  }) {
    final result = create();
    if (connectAck != null) result.connectAck = connectAck;
    if (delivery != null) result.delivery = delivery;
    if (ack != null) result.ack = ack;
    if (error != null) result.error = error;
    if (heartbeatPong != null) result.heartbeatPong = heartbeatPong;
    return result;
  }

  ServerFrame._();

  factory ServerFrame.fromBuffer($core.List<$core.int> data,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromBuffer(data, registry);
  factory ServerFrame.fromJson($core.String json,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromJson(json, registry);

  static const $core.Map<$core.int, ServerFrame_Payload>
      _ServerFrame_PayloadByTag = {
    1: ServerFrame_Payload.connectAck,
    2: ServerFrame_Payload.delivery,
    3: ServerFrame_Payload.ack,
    4: ServerFrame_Payload.error,
    5: ServerFrame_Payload.heartbeatPong,
    0: ServerFrame_Payload.notSet
  };
  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
      _omitMessageNames ? '' : 'ServerFrame',
      package:
          const $pb.PackageName(_omitMessageNames ? '' : 'mics.message.v1'),
      createEmptyInstance: create)
    ..oo(0, [1, 2, 3, 4, 5])
    ..aOM<ConnectAck>(1, _omitFieldNames ? '' : 'connectAck',
        subBuilder: ConnectAck.create)
    ..aOM<MessageDelivery>(2, _omitFieldNames ? '' : 'delivery',
        subBuilder: MessageDelivery.create)
    ..aOM<MessageAck>(3, _omitFieldNames ? '' : 'ack',
        subBuilder: MessageAck.create)
    ..aOM<ServerError>(4, _omitFieldNames ? '' : 'error',
        subBuilder: ServerError.create)
    ..aOM<HeartbeatPong>(5, _omitFieldNames ? '' : 'heartbeatPong',
        subBuilder: HeartbeatPong.create)
    ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  ServerFrame clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  ServerFrame copyWith(void Function(ServerFrame) updates) =>
      super.copyWith((message) => updates(message as ServerFrame))
          as ServerFrame;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static ServerFrame create() => ServerFrame._();
  @$core.override
  ServerFrame createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static ServerFrame getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<ServerFrame>(create);
  static ServerFrame? _defaultInstance;

  @$pb.TagNumber(1)
  @$pb.TagNumber(2)
  @$pb.TagNumber(3)
  @$pb.TagNumber(4)
  @$pb.TagNumber(5)
  ServerFrame_Payload whichPayload() =>
      _ServerFrame_PayloadByTag[$_whichOneof(0)]!;
  @$pb.TagNumber(1)
  @$pb.TagNumber(2)
  @$pb.TagNumber(3)
  @$pb.TagNumber(4)
  @$pb.TagNumber(5)
  void clearPayload() => $_clearField($_whichOneof(0));

  @$pb.TagNumber(1)
  ConnectAck get connectAck => $_getN(0);
  @$pb.TagNumber(1)
  set connectAck(ConnectAck value) => $_setField(1, value);
  @$pb.TagNumber(1)
  $core.bool hasConnectAck() => $_has(0);
  @$pb.TagNumber(1)
  void clearConnectAck() => $_clearField(1);
  @$pb.TagNumber(1)
  ConnectAck ensureConnectAck() => $_ensure(0);

  @$pb.TagNumber(2)
  MessageDelivery get delivery => $_getN(1);
  @$pb.TagNumber(2)
  set delivery(MessageDelivery value) => $_setField(2, value);
  @$pb.TagNumber(2)
  $core.bool hasDelivery() => $_has(1);
  @$pb.TagNumber(2)
  void clearDelivery() => $_clearField(2);
  @$pb.TagNumber(2)
  MessageDelivery ensureDelivery() => $_ensure(1);

  @$pb.TagNumber(3)
  MessageAck get ack => $_getN(2);
  @$pb.TagNumber(3)
  set ack(MessageAck value) => $_setField(3, value);
  @$pb.TagNumber(3)
  $core.bool hasAck() => $_has(2);
  @$pb.TagNumber(3)
  void clearAck() => $_clearField(3);
  @$pb.TagNumber(3)
  MessageAck ensureAck() => $_ensure(2);

  @$pb.TagNumber(4)
  ServerError get error => $_getN(3);
  @$pb.TagNumber(4)
  set error(ServerError value) => $_setField(4, value);
  @$pb.TagNumber(4)
  $core.bool hasError() => $_has(3);
  @$pb.TagNumber(4)
  void clearError() => $_clearField(4);
  @$pb.TagNumber(4)
  ServerError ensureError() => $_ensure(3);

  @$pb.TagNumber(5)
  HeartbeatPong get heartbeatPong => $_getN(4);
  @$pb.TagNumber(5)
  set heartbeatPong(HeartbeatPong value) => $_setField(5, value);
  @$pb.TagNumber(5)
  $core.bool hasHeartbeatPong() => $_has(4);
  @$pb.TagNumber(5)
  void clearHeartbeatPong() => $_clearField(5);
  @$pb.TagNumber(5)
  HeartbeatPong ensureHeartbeatPong() => $_ensure(4);
}

class HeartbeatPing extends $pb.GeneratedMessage {
  factory HeartbeatPing({
    $fixnum.Int64? timestampMs,
  }) {
    final result = create();
    if (timestampMs != null) result.timestampMs = timestampMs;
    return result;
  }

  HeartbeatPing._();

  factory HeartbeatPing.fromBuffer($core.List<$core.int> data,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromBuffer(data, registry);
  factory HeartbeatPing.fromJson($core.String json,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
      _omitMessageNames ? '' : 'HeartbeatPing',
      package:
          const $pb.PackageName(_omitMessageNames ? '' : 'mics.message.v1'),
      createEmptyInstance: create)
    ..aInt64(1, _omitFieldNames ? '' : 'timestampMs')
    ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  HeartbeatPing clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  HeartbeatPing copyWith(void Function(HeartbeatPing) updates) =>
      super.copyWith((message) => updates(message as HeartbeatPing))
          as HeartbeatPing;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static HeartbeatPing create() => HeartbeatPing._();
  @$core.override
  HeartbeatPing createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static HeartbeatPing getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<HeartbeatPing>(create);
  static HeartbeatPing? _defaultInstance;

  @$pb.TagNumber(1)
  $fixnum.Int64 get timestampMs => $_getI64(0);
  @$pb.TagNumber(1)
  set timestampMs($fixnum.Int64 value) => $_setInt64(0, value);
  @$pb.TagNumber(1)
  $core.bool hasTimestampMs() => $_has(0);
  @$pb.TagNumber(1)
  void clearTimestampMs() => $_clearField(1);
}

class HeartbeatPong extends $pb.GeneratedMessage {
  factory HeartbeatPong({
    $fixnum.Int64? timestampMs,
  }) {
    final result = create();
    if (timestampMs != null) result.timestampMs = timestampMs;
    return result;
  }

  HeartbeatPong._();

  factory HeartbeatPong.fromBuffer($core.List<$core.int> data,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromBuffer(data, registry);
  factory HeartbeatPong.fromJson($core.String json,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
      _omitMessageNames ? '' : 'HeartbeatPong',
      package:
          const $pb.PackageName(_omitMessageNames ? '' : 'mics.message.v1'),
      createEmptyInstance: create)
    ..aInt64(1, _omitFieldNames ? '' : 'timestampMs')
    ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  HeartbeatPong clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  HeartbeatPong copyWith(void Function(HeartbeatPong) updates) =>
      super.copyWith((message) => updates(message as HeartbeatPong))
          as HeartbeatPong;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static HeartbeatPong create() => HeartbeatPong._();
  @$core.override
  HeartbeatPong createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static HeartbeatPong getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<HeartbeatPong>(create);
  static HeartbeatPong? _defaultInstance;

  @$pb.TagNumber(1)
  $fixnum.Int64 get timestampMs => $_getI64(0);
  @$pb.TagNumber(1)
  set timestampMs($fixnum.Int64 value) => $_setInt64(0, value);
  @$pb.TagNumber(1)
  $core.bool hasTimestampMs() => $_has(0);
  @$pb.TagNumber(1)
  void clearTimestampMs() => $_clearField(1);
}

const $core.bool _omitFieldNames =
    $core.bool.fromEnvironment('protobuf.omit_field_names');
const $core.bool _omitMessageNames =
    $core.bool.fromEnvironment('protobuf.omit_message_names');
