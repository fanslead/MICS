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

import 'package:protobuf/protobuf.dart' as $pb;

class MessageType extends $pb.ProtobufEnum {
  static const MessageType SINGLE_CHAT =
      MessageType._(0, _omitEnumNames ? '' : 'SINGLE_CHAT');
  static const MessageType GROUP_CHAT =
      MessageType._(1, _omitEnumNames ? '' : 'GROUP_CHAT');

  static const $core.List<MessageType> values = <MessageType>[
    SINGLE_CHAT,
    GROUP_CHAT,
  ];

  static final $core.List<MessageType?> _byValue =
      $pb.ProtobufEnum.$_initByValueList(values, 1);
  static MessageType? valueOf($core.int value) =>
      value < 0 || value >= _byValue.length ? null : _byValue[value];

  const MessageType._(super.value, super.name);
}

class AckStatus extends $pb.ProtobufEnum {
  static const AckStatus SENT = AckStatus._(0, _omitEnumNames ? '' : 'SENT');
  static const AckStatus FAILED =
      AckStatus._(1, _omitEnumNames ? '' : 'FAILED');

  static const $core.List<AckStatus> values = <AckStatus>[
    SENT,
    FAILED,
  ];

  static final $core.List<AckStatus?> _byValue =
      $pb.ProtobufEnum.$_initByValueList(values, 1);
  static AckStatus? valueOf($core.int value) =>
      value < 0 || value >= _byValue.length ? null : _byValue[value];

  const AckStatus._(super.value, super.name);
}

const $core.bool _omitEnumNames =
    $core.bool.fromEnvironment('protobuf.omit_enum_names');
