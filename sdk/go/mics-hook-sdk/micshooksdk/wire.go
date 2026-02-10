package micshooksdk

import (
	"errors"
	"fmt"

	"google.golang.org/protobuf/encoding/protowire"
)

var (
	errInvalidProtobuf = errors.New("invalid protobuf")
)

// ---- HookMeta (mics.hook.v1.HookMeta) ----
// Fields:
// 1 tenant_id (string)
// 2 request_id (string)
// 3 timestamp_ms (int64)
// 4 sign (string)
// 5 trace_id (string)
func decodeHookMeta(b []byte) (HookMeta, error) {
	var m HookMeta
	for len(b) > 0 {
		num, typ, n := protowire.ConsumeTag(b)
		if n < 0 {
			return HookMeta{}, errInvalidProtobuf
		}
		b = b[n:]

		switch num {
		case 1: // tenant_id
			v, n := protowire.ConsumeString(b)
			if n < 0 {
				return HookMeta{}, errInvalidProtobuf
			}
			m.TenantID = v
			b = b[n:]
		case 2: // request_id
			v, n := protowire.ConsumeString(b)
			if n < 0 {
				return HookMeta{}, errInvalidProtobuf
			}
			m.RequestID = v
			b = b[n:]
		case 3: // timestamp_ms
			v, n := protowire.ConsumeVarint(b)
			if n < 0 {
				return HookMeta{}, errInvalidProtobuf
			}
			m.TimestampMs = int64(v)
			b = b[n:]
		case 4: // sign
			v, n := protowire.ConsumeString(b)
			if n < 0 {
				return HookMeta{}, errInvalidProtobuf
			}
			m.Sign = v
			b = b[n:]
		case 5: // trace_id
			v, n := protowire.ConsumeString(b)
			if n < 0 {
				return HookMeta{}, errInvalidProtobuf
			}
			m.TraceID = v
			b = b[n:]
		default:
			n := protowire.ConsumeFieldValue(num, typ, b)
			if n < 0 {
				return HookMeta{}, errInvalidProtobuf
			}
			b = b[n:]
		}
	}
	return m, nil
}

func appendHookMeta(dst []byte, meta HookMeta) []byte {
	if meta.TenantID != "" {
		dst = protowire.AppendTag(dst, 1, protowire.BytesType)
		dst = protowire.AppendString(dst, meta.TenantID)
	}
	if meta.RequestID != "" {
		dst = protowire.AppendTag(dst, 2, protowire.BytesType)
		dst = protowire.AppendString(dst, meta.RequestID)
	}
	if meta.TimestampMs != 0 {
		dst = protowire.AppendTag(dst, 3, protowire.VarintType)
		dst = protowire.AppendVarint(dst, uint64(meta.TimestampMs))
	}
	if meta.Sign != "" {
		dst = protowire.AppendTag(dst, 4, protowire.BytesType)
		dst = protowire.AppendString(dst, meta.Sign)
	}
	if meta.TraceID != "" {
		dst = protowire.AppendTag(dst, 5, protowire.BytesType)
		dst = protowire.AppendString(dst, meta.TraceID)
	}
	return dst
}

// ---- AuthRequest (meta=1, token=2, device_id=3) ----
func DecodeAuthRequest(b []byte) (AuthRequest, error) {
	var req AuthRequest
	for len(b) > 0 {
		num, typ, n := protowire.ConsumeTag(b)
		if n < 0 {
			return AuthRequest{}, errInvalidProtobuf
		}
		b = b[n:]

		switch num {
		case 1: // meta
			v, n := protowire.ConsumeBytes(b)
			if n < 0 {
				return AuthRequest{}, errInvalidProtobuf
			}
			meta, err := decodeHookMeta(v)
			if err != nil {
				return AuthRequest{}, err
			}
			req.Meta = meta
			b = b[n:]
		case 2: // token
			v, n := protowire.ConsumeString(b)
			if n < 0 {
				return AuthRequest{}, errInvalidProtobuf
			}
			req.Token = v
			b = b[n:]
		case 3: // device_id
			v, n := protowire.ConsumeString(b)
			if n < 0 {
				return AuthRequest{}, errInvalidProtobuf
			}
			req.DeviceID = v
			b = b[n:]
		default:
			n := protowire.ConsumeFieldValue(num, typ, b)
			if n < 0 {
				return AuthRequest{}, errInvalidProtobuf
			}
			b = b[n:]
		}
	}
	return req, nil
}

func EncodeAuthRequest(req AuthRequest) []byte {
	var out []byte
	out = protowire.AppendTag(out, 1, protowire.BytesType)
	out = protowire.AppendBytes(out, appendHookMeta(nil, req.Meta))
	if req.Token != "" {
		out = protowire.AppendTag(out, 2, protowire.BytesType)
		out = protowire.AppendString(out, req.Token)
	}
	if req.DeviceID != "" {
		out = protowire.AppendTag(out, 3, protowire.BytesType)
		out = protowire.AppendString(out, req.DeviceID)
	}
	return out
}

// ---- CheckMessageRequest (meta=1, message=2) ----
func DecodeCheckMessageRequest(b []byte) (CheckMessageRequest, error) {
	var req CheckMessageRequest
	for len(b) > 0 {
		num, typ, n := protowire.ConsumeTag(b)
		if n < 0 {
			return CheckMessageRequest{}, errInvalidProtobuf
		}
		b = b[n:]

		switch num {
		case 1: // meta
			v, n := protowire.ConsumeBytes(b)
			if n < 0 {
				return CheckMessageRequest{}, errInvalidProtobuf
			}
			meta, err := decodeHookMeta(v)
			if err != nil {
				return CheckMessageRequest{}, err
			}
			req.Meta = meta
			b = b[n:]
		case 2: // message (opaque bytes)
			v, n := protowire.ConsumeBytes(b)
			if n < 0 {
				return CheckMessageRequest{}, errInvalidProtobuf
			}
			req.MessageWireBytes = append(req.MessageWireBytes[:0], v...)
			b = b[n:]
		default:
			n := protowire.ConsumeFieldValue(num, typ, b)
			if n < 0 {
				return CheckMessageRequest{}, errInvalidProtobuf
			}
			b = b[n:]
		}
	}
	return req, nil
}

func EncodeCheckMessageRequest(req CheckMessageRequest) []byte {
	var out []byte
	out = protowire.AppendTag(out, 1, protowire.BytesType)
	out = protowire.AppendBytes(out, appendHookMeta(nil, req.Meta))
	if len(req.MessageWireBytes) > 0 {
		out = protowire.AppendTag(out, 2, protowire.BytesType)
		out = protowire.AppendBytes(out, req.MessageWireBytes)
	}
	return out
}

// ---- GetGroupMembersRequest (meta=1, group_id=2) ----
func DecodeGetGroupMembersRequest(b []byte) (GetGroupMembersRequest, error) {
	var req GetGroupMembersRequest
	for len(b) > 0 {
		num, typ, n := protowire.ConsumeTag(b)
		if n < 0 {
			return GetGroupMembersRequest{}, errInvalidProtobuf
		}
		b = b[n:]

		switch num {
		case 1:
			v, n := protowire.ConsumeBytes(b)
			if n < 0 {
				return GetGroupMembersRequest{}, errInvalidProtobuf
			}
			meta, err := decodeHookMeta(v)
			if err != nil {
				return GetGroupMembersRequest{}, err
			}
			req.Meta = meta
			b = b[n:]
		case 2:
			v, n := protowire.ConsumeString(b)
			if n < 0 {
				return GetGroupMembersRequest{}, errInvalidProtobuf
			}
			req.GroupID = v
			b = b[n:]
		default:
			n := protowire.ConsumeFieldValue(num, typ, b)
			if n < 0 {
				return GetGroupMembersRequest{}, errInvalidProtobuf
			}
			b = b[n:]
		}
	}
	return req, nil
}

func EncodeGetGroupMembersRequest(req GetGroupMembersRequest) []byte {
	var out []byte
	out = protowire.AppendTag(out, 1, protowire.BytesType)
	out = protowire.AppendBytes(out, appendHookMeta(nil, req.Meta))
	if req.GroupID != "" {
		out = protowire.AppendTag(out, 2, protowire.BytesType)
		out = protowire.AppendString(out, req.GroupID)
	}
	return out
}

func must(err error) {
	if err != nil {
		panic(err)
	}
}

func wrapDecodeErr(name string, err error) error {
	return fmt.Errorf("%s: %w", name, err)
}

