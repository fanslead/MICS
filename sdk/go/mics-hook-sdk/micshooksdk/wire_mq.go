package micshooksdk

import "google.golang.org/protobuf/encoding/protowire"

func decodeMqEvent(b []byte) (MqEvent, error) {
	var evt MqEvent
	for len(b) > 0 {
		num, typ, n := protowire.ConsumeTag(b)
		if n < 0 {
			return MqEvent{}, errInvalidProtobuf
		}
		b = b[n:]

		switch num {
		case 1: // tenant_id
			v, n := protowire.ConsumeString(b)
			if n < 0 {
				return MqEvent{}, errInvalidProtobuf
			}
			evt.TenantID = v
			b = b[n:]
		case 2: // event_type
			v, n := protowire.ConsumeVarint(b)
			if n < 0 {
				return MqEvent{}, errInvalidProtobuf
			}
			evt.EventType = EventType(v)
			b = b[n:]
		case 3: // msg_id
			v, n := protowire.ConsumeString(b)
			if n < 0 {
				return MqEvent{}, errInvalidProtobuf
			}
			evt.MsgID = v
			b = b[n:]
		case 4: // user_id
			v, n := protowire.ConsumeString(b)
			if n < 0 {
				return MqEvent{}, errInvalidProtobuf
			}
			evt.UserID = v
			b = b[n:]
		case 5: // device_id
			v, n := protowire.ConsumeString(b)
			if n < 0 {
				return MqEvent{}, errInvalidProtobuf
			}
			evt.DeviceID = v
			b = b[n:]
		case 6: // to_user_id
			v, n := protowire.ConsumeString(b)
			if n < 0 {
				return MqEvent{}, errInvalidProtobuf
			}
			evt.ToUserID = v
			b = b[n:]
		case 7: // group_id
			v, n := protowire.ConsumeString(b)
			if n < 0 {
				return MqEvent{}, errInvalidProtobuf
			}
			evt.GroupID = v
			b = b[n:]
		case 8: // event_data
			v, n := protowire.ConsumeBytes(b)
			if n < 0 {
				return MqEvent{}, errInvalidProtobuf
			}
			evt.EventData = append(evt.EventData[:0], v...)
			b = b[n:]
		case 9: // timestamp
			v, n := protowire.ConsumeVarint(b)
			if n < 0 {
				return MqEvent{}, errInvalidProtobuf
			}
			evt.Timestamp = int64(v)
			b = b[n:]
		case 10: // node_id
			v, n := protowire.ConsumeString(b)
			if n < 0 {
				return MqEvent{}, errInvalidProtobuf
			}
			evt.NodeID = v
			b = b[n:]
		case 11: // sign
			v, n := protowire.ConsumeString(b)
			if n < 0 {
				return MqEvent{}, errInvalidProtobuf
			}
			evt.Sign = v
			b = b[n:]
		case 12: // trace_id
			v, n := protowire.ConsumeString(b)
			if n < 0 {
				return MqEvent{}, errInvalidProtobuf
			}
			evt.TraceID = v
			b = b[n:]
		default:
			n := protowire.ConsumeFieldValue(num, typ, b)
			if n < 0 {
				return MqEvent{}, errInvalidProtobuf
			}
			b = b[n:]
		}
	}
	return evt, nil
}

func encodeMqEvent(evt MqEvent) []byte {
	var out []byte
	if evt.TenantID != "" {
		out = protowire.AppendTag(out, 1, protowire.BytesType)
		out = protowire.AppendString(out, evt.TenantID)
	}
	if evt.EventType != 0 {
		out = protowire.AppendTag(out, 2, protowire.VarintType)
		out = protowire.AppendVarint(out, uint64(evt.EventType))
	}
	if evt.MsgID != "" {
		out = protowire.AppendTag(out, 3, protowire.BytesType)
		out = protowire.AppendString(out, evt.MsgID)
	}
	if evt.UserID != "" {
		out = protowire.AppendTag(out, 4, protowire.BytesType)
		out = protowire.AppendString(out, evt.UserID)
	}
	if evt.DeviceID != "" {
		out = protowire.AppendTag(out, 5, protowire.BytesType)
		out = protowire.AppendString(out, evt.DeviceID)
	}
	if evt.ToUserID != "" {
		out = protowire.AppendTag(out, 6, protowire.BytesType)
		out = protowire.AppendString(out, evt.ToUserID)
	}
	if evt.GroupID != "" {
		out = protowire.AppendTag(out, 7, protowire.BytesType)
		out = protowire.AppendString(out, evt.GroupID)
	}
	if len(evt.EventData) > 0 {
		out = protowire.AppendTag(out, 8, protowire.BytesType)
		out = protowire.AppendBytes(out, evt.EventData)
	}
	if evt.Timestamp != 0 {
		out = protowire.AppendTag(out, 9, protowire.VarintType)
		out = protowire.AppendVarint(out, uint64(evt.Timestamp))
	}
	if evt.NodeID != "" {
		out = protowire.AppendTag(out, 10, protowire.BytesType)
		out = protowire.AppendString(out, evt.NodeID)
	}
	if evt.Sign != "" {
		out = protowire.AppendTag(out, 11, protowire.BytesType)
		out = protowire.AppendString(out, evt.Sign)
	}
	if evt.TraceID != "" {
		out = protowire.AppendTag(out, 12, protowire.BytesType)
		out = protowire.AppendString(out, evt.TraceID)
	}
	return out
}

