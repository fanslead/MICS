// This is a generated file - do not edit.
//
// Generated from Protos/mics_message.proto.

// @dart = 3.3

// ignore_for_file: annotate_overrides, camel_case_types, comment_references
// ignore_for_file: constant_identifier_names
// ignore_for_file: curly_braces_in_flow_control_structures
// ignore_for_file: deprecated_member_use_from_same_package, library_prefixes
// ignore_for_file: non_constant_identifier_names, prefer_relative_imports
// ignore_for_file: unused_import

import 'dart:convert' as $convert;
import 'dart:core' as $core;
import 'dart:typed_data' as $typed_data;

@$core.Deprecated('Use messageTypeDescriptor instead')
const MessageType$json = {
  '1': 'MessageType',
  '2': [
    {'1': 'SINGLE_CHAT', '2': 0},
    {'1': 'GROUP_CHAT', '2': 1},
  ],
};

/// Descriptor for `MessageType`. Decode as a `google.protobuf.EnumDescriptorProto`.
final $typed_data.Uint8List messageTypeDescriptor = $convert.base64Decode(
    'CgtNZXNzYWdlVHlwZRIPCgtTSU5HTEVfQ0hBVBAAEg4KCkdST1VQX0NIQVQQAQ==');

@$core.Deprecated('Use ackStatusDescriptor instead')
const AckStatus$json = {
  '1': 'AckStatus',
  '2': [
    {'1': 'SENT', '2': 0},
    {'1': 'FAILED', '2': 1},
  ],
};

/// Descriptor for `AckStatus`. Decode as a `google.protobuf.EnumDescriptorProto`.
final $typed_data.Uint8List ackStatusDescriptor =
    $convert.base64Decode('CglBY2tTdGF0dXMSCAoEU0VOVBAAEgoKBkZBSUxFRBAB');

@$core.Deprecated('Use messageRequestDescriptor instead')
const MessageRequest$json = {
  '1': 'MessageRequest',
  '2': [
    {'1': 'tenant_id', '3': 1, '4': 1, '5': 9, '10': 'tenantId'},
    {'1': 'user_id', '3': 2, '4': 1, '5': 9, '10': 'userId'},
    {'1': 'device_id', '3': 3, '4': 1, '5': 9, '10': 'deviceId'},
    {'1': 'msg_id', '3': 4, '4': 1, '5': 9, '10': 'msgId'},
    {
      '1': 'msg_type',
      '3': 5,
      '4': 1,
      '5': 14,
      '6': '.mics.message.v1.MessageType',
      '10': 'msgType'
    },
    {'1': 'to_user_id', '3': 6, '4': 1, '5': 9, '10': 'toUserId'},
    {'1': 'group_id', '3': 7, '4': 1, '5': 9, '10': 'groupId'},
    {'1': 'msg_body', '3': 8, '4': 1, '5': 12, '10': 'msgBody'},
    {'1': 'timestamp_ms', '3': 9, '4': 1, '5': 3, '10': 'timestampMs'},
  ],
};

/// Descriptor for `MessageRequest`. Decode as a `google.protobuf.DescriptorProto`.
final $typed_data.Uint8List messageRequestDescriptor = $convert.base64Decode(
    'Cg5NZXNzYWdlUmVxdWVzdBIbCgl0ZW5hbnRfaWQYASABKAlSCHRlbmFudElkEhcKB3VzZXJfaW'
    'QYAiABKAlSBnVzZXJJZBIbCglkZXZpY2VfaWQYAyABKAlSCGRldmljZUlkEhUKBm1zZ19pZBgE'
    'IAEoCVIFbXNnSWQSNwoIbXNnX3R5cGUYBSABKA4yHC5taWNzLm1lc3NhZ2UudjEuTWVzc2FnZV'
    'R5cGVSB21zZ1R5cGUSHAoKdG9fdXNlcl9pZBgGIAEoCVIIdG9Vc2VySWQSGQoIZ3JvdXBfaWQY'
    'ByABKAlSB2dyb3VwSWQSGQoIbXNnX2JvZHkYCCABKAxSB21zZ0JvZHkSIQoMdGltZXN0YW1wX2'
    '1zGAkgASgDUgt0aW1lc3RhbXBNcw==');

@$core.Deprecated('Use messageAckDescriptor instead')
const MessageAck$json = {
  '1': 'MessageAck',
  '2': [
    {'1': 'msg_id', '3': 1, '4': 1, '5': 9, '10': 'msgId'},
    {
      '1': 'status',
      '3': 2,
      '4': 1,
      '5': 14,
      '6': '.mics.message.v1.AckStatus',
      '10': 'status'
    },
    {'1': 'timestamp_ms', '3': 3, '4': 1, '5': 3, '10': 'timestampMs'},
    {'1': 'reason', '3': 4, '4': 1, '5': 9, '10': 'reason'},
  ],
};

/// Descriptor for `MessageAck`. Decode as a `google.protobuf.DescriptorProto`.
final $typed_data.Uint8List messageAckDescriptor = $convert.base64Decode(
    'CgpNZXNzYWdlQWNrEhUKBm1zZ19pZBgBIAEoCVIFbXNnSWQSMgoGc3RhdHVzGAIgASgOMhoubW'
    'ljcy5tZXNzYWdlLnYxLkFja1N0YXR1c1IGc3RhdHVzEiEKDHRpbWVzdGFtcF9tcxgDIAEoA1IL'
    'dGltZXN0YW1wTXMSFgoGcmVhc29uGAQgASgJUgZyZWFzb24=');

@$core.Deprecated('Use connectAckDescriptor instead')
const ConnectAck$json = {
  '1': 'ConnectAck',
  '2': [
    {'1': 'code', '3': 1, '4': 1, '5': 5, '10': 'code'},
    {'1': 'tenant_id', '3': 2, '4': 1, '5': 9, '10': 'tenantId'},
    {'1': 'user_id', '3': 3, '4': 1, '5': 9, '10': 'userId'},
    {'1': 'device_id', '3': 4, '4': 1, '5': 9, '10': 'deviceId'},
    {'1': 'node_id', '3': 5, '4': 1, '5': 9, '10': 'nodeId'},
    {'1': 'trace_id', '3': 6, '4': 1, '5': 9, '10': 'traceId'},
  ],
};

/// Descriptor for `ConnectAck`. Decode as a `google.protobuf.DescriptorProto`.
final $typed_data.Uint8List connectAckDescriptor = $convert.base64Decode(
    'CgpDb25uZWN0QWNrEhIKBGNvZGUYASABKAVSBGNvZGUSGwoJdGVuYW50X2lkGAIgASgJUgh0ZW'
    '5hbnRJZBIXCgd1c2VyX2lkGAMgASgJUgZ1c2VySWQSGwoJZGV2aWNlX2lkGAQgASgJUghkZXZp'
    'Y2VJZBIXCgdub2RlX2lkGAUgASgJUgZub2RlSWQSGQoIdHJhY2VfaWQYBiABKAlSB3RyYWNlSW'
    'Q=');

@$core.Deprecated('Use messageDeliveryDescriptor instead')
const MessageDelivery$json = {
  '1': 'MessageDelivery',
  '2': [
    {
      '1': 'message',
      '3': 1,
      '4': 1,
      '5': 11,
      '6': '.mics.message.v1.MessageRequest',
      '10': 'message'
    },
  ],
};

/// Descriptor for `MessageDelivery`. Decode as a `google.protobuf.DescriptorProto`.
final $typed_data.Uint8List messageDeliveryDescriptor = $convert.base64Decode(
    'Cg9NZXNzYWdlRGVsaXZlcnkSOQoHbWVzc2FnZRgBIAEoCzIfLm1pY3MubWVzc2FnZS52MS5NZX'
    'NzYWdlUmVxdWVzdFIHbWVzc2FnZQ==');

@$core.Deprecated('Use serverErrorDescriptor instead')
const ServerError$json = {
  '1': 'ServerError',
  '2': [
    {'1': 'code', '3': 1, '4': 1, '5': 5, '10': 'code'},
    {'1': 'message', '3': 2, '4': 1, '5': 9, '10': 'message'},
  ],
};

/// Descriptor for `ServerError`. Decode as a `google.protobuf.DescriptorProto`.
final $typed_data.Uint8List serverErrorDescriptor = $convert.base64Decode(
    'CgtTZXJ2ZXJFcnJvchISCgRjb2RlGAEgASgFUgRjb2RlEhgKB21lc3NhZ2UYAiABKAlSB21lc3'
    'NhZ2U=');

@$core.Deprecated('Use clientFrameDescriptor instead')
const ClientFrame$json = {
  '1': 'ClientFrame',
  '2': [
    {
      '1': 'message',
      '3': 1,
      '4': 1,
      '5': 11,
      '6': '.mics.message.v1.MessageRequest',
      '9': 0,
      '10': 'message'
    },
    {
      '1': 'heartbeat_ping',
      '3': 2,
      '4': 1,
      '5': 11,
      '6': '.mics.message.v1.HeartbeatPing',
      '9': 0,
      '10': 'heartbeatPing'
    },
  ],
  '8': [
    {'1': 'payload'},
  ],
};

/// Descriptor for `ClientFrame`. Decode as a `google.protobuf.DescriptorProto`.
final $typed_data.Uint8List clientFrameDescriptor = $convert.base64Decode(
    'CgtDbGllbnRGcmFtZRI7CgdtZXNzYWdlGAEgASgLMh8ubWljcy5tZXNzYWdlLnYxLk1lc3NhZ2'
    'VSZXF1ZXN0SABSB21lc3NhZ2USRwoOaGVhcnRiZWF0X3BpbmcYAiABKAsyHi5taWNzLm1lc3Nh'
    'Z2UudjEuSGVhcnRiZWF0UGluZ0gAUg1oZWFydGJlYXRQaW5nQgkKB3BheWxvYWQ=');

@$core.Deprecated('Use serverFrameDescriptor instead')
const ServerFrame$json = {
  '1': 'ServerFrame',
  '2': [
    {
      '1': 'connect_ack',
      '3': 1,
      '4': 1,
      '5': 11,
      '6': '.mics.message.v1.ConnectAck',
      '9': 0,
      '10': 'connectAck'
    },
    {
      '1': 'delivery',
      '3': 2,
      '4': 1,
      '5': 11,
      '6': '.mics.message.v1.MessageDelivery',
      '9': 0,
      '10': 'delivery'
    },
    {
      '1': 'ack',
      '3': 3,
      '4': 1,
      '5': 11,
      '6': '.mics.message.v1.MessageAck',
      '9': 0,
      '10': 'ack'
    },
    {
      '1': 'error',
      '3': 4,
      '4': 1,
      '5': 11,
      '6': '.mics.message.v1.ServerError',
      '9': 0,
      '10': 'error'
    },
    {
      '1': 'heartbeat_pong',
      '3': 5,
      '4': 1,
      '5': 11,
      '6': '.mics.message.v1.HeartbeatPong',
      '9': 0,
      '10': 'heartbeatPong'
    },
  ],
  '8': [
    {'1': 'payload'},
  ],
};

/// Descriptor for `ServerFrame`. Decode as a `google.protobuf.DescriptorProto`.
final $typed_data.Uint8List serverFrameDescriptor = $convert.base64Decode(
    'CgtTZXJ2ZXJGcmFtZRI+Cgtjb25uZWN0X2FjaxgBIAEoCzIbLm1pY3MubWVzc2FnZS52MS5Db2'
    '5uZWN0QWNrSABSCmNvbm5lY3RBY2sSPgoIZGVsaXZlcnkYAiABKAsyIC5taWNzLm1lc3NhZ2Uu'
    'djEuTWVzc2FnZURlbGl2ZXJ5SABSCGRlbGl2ZXJ5Ei8KA2FjaxgDIAEoCzIbLm1pY3MubWVzc2'
    'FnZS52MS5NZXNzYWdlQWNrSABSA2FjaxI0CgVlcnJvchgEIAEoCzIcLm1pY3MubWVzc2FnZS52'
    'MS5TZXJ2ZXJFcnJvckgAUgVlcnJvchJHCg5oZWFydGJlYXRfcG9uZxgFIAEoCzIeLm1pY3MubW'
    'Vzc2FnZS52MS5IZWFydGJlYXRQb25nSABSDWhlYXJ0YmVhdFBvbmdCCQoHcGF5bG9hZA==');

@$core.Deprecated('Use heartbeatPingDescriptor instead')
const HeartbeatPing$json = {
  '1': 'HeartbeatPing',
  '2': [
    {'1': 'timestamp_ms', '3': 1, '4': 1, '5': 3, '10': 'timestampMs'},
  ],
};

/// Descriptor for `HeartbeatPing`. Decode as a `google.protobuf.DescriptorProto`.
final $typed_data.Uint8List heartbeatPingDescriptor = $convert.base64Decode(
    'Cg1IZWFydGJlYXRQaW5nEiEKDHRpbWVzdGFtcF9tcxgBIAEoA1ILdGltZXN0YW1wTXM=');

@$core.Deprecated('Use heartbeatPongDescriptor instead')
const HeartbeatPong$json = {
  '1': 'HeartbeatPong',
  '2': [
    {'1': 'timestamp_ms', '3': 1, '4': 1, '5': 3, '10': 'timestampMs'},
  ],
};

/// Descriptor for `HeartbeatPong`. Decode as a `google.protobuf.DescriptorProto`.
final $typed_data.Uint8List heartbeatPongDescriptor = $convert.base64Decode(
    'Cg1IZWFydGJlYXRQb25nEiEKDHRpbWVzdGFtcF9tcxgBIAEoA1ILdGltZXN0YW1wTXM=');
