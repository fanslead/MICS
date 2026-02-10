package micshooksdk

import "google.golang.org/protobuf/encoding/protowire"

// ---- TenantRuntimeConfig (mics.hook.v1.TenantRuntimeConfig) ----
// Fields:
// 1 hook_base_url (string)
// 2 heartbeat_timeout_seconds (int32)
// 3 offline_buffer_ttl_seconds (int32)
// 4 tenant_max_connections (int32)
// 5 user_max_connections (int32)
// 6 tenant_max_message_qps (int32)
// 7 tenant_secret (string)
// 8..12 optional overrides
func appendTenantRuntimeConfig(dst []byte, cfg TenantRuntimeConfig) []byte {
	if cfg.HookBaseURL != "" {
		dst = protowire.AppendTag(dst, 1, protowire.BytesType)
		dst = protowire.AppendString(dst, cfg.HookBaseURL)
	}
	if cfg.HeartbeatTimeoutSeconds != 0 {
		dst = protowire.AppendTag(dst, 2, protowire.VarintType)
		dst = protowire.AppendVarint(dst, uint64(cfg.HeartbeatTimeoutSeconds))
	}
	if cfg.OfflineBufferTTLSeconds != 0 {
		dst = protowire.AppendTag(dst, 3, protowire.VarintType)
		dst = protowire.AppendVarint(dst, uint64(cfg.OfflineBufferTTLSeconds))
	}
	if cfg.TenantMaxConnections != 0 {
		dst = protowire.AppendTag(dst, 4, protowire.VarintType)
		dst = protowire.AppendVarint(dst, uint64(cfg.TenantMaxConnections))
	}
	if cfg.UserMaxConnections != 0 {
		dst = protowire.AppendTag(dst, 5, protowire.VarintType)
		dst = protowire.AppendVarint(dst, uint64(cfg.UserMaxConnections))
	}
	if cfg.TenantMaxMessageQps != 0 {
		dst = protowire.AppendTag(dst, 6, protowire.VarintType)
		dst = protowire.AppendVarint(dst, uint64(cfg.TenantMaxMessageQps))
	}
	if cfg.TenantSecret != "" {
		dst = protowire.AppendTag(dst, 7, protowire.BytesType)
		dst = protowire.AppendString(dst, cfg.TenantSecret)
	}

	if cfg.HookMaxConcurrency != nil {
		dst = protowire.AppendTag(dst, 8, protowire.VarintType)
		dst = protowire.AppendVarint(dst, uint64(*cfg.HookMaxConcurrency))
	}
	if cfg.HookQueueTimeoutMs != nil {
		dst = protowire.AppendTag(dst, 9, protowire.VarintType)
		dst = protowire.AppendVarint(dst, uint64(*cfg.HookQueueTimeoutMs))
	}
	if cfg.HookBreakerFailureThreshold != nil {
		dst = protowire.AppendTag(dst, 10, protowire.VarintType)
		dst = protowire.AppendVarint(dst, uint64(*cfg.HookBreakerFailureThreshold))
	}
	if cfg.HookBreakerOpenMs != nil {
		dst = protowire.AppendTag(dst, 11, protowire.VarintType)
		dst = protowire.AppendVarint(dst, uint64(*cfg.HookBreakerOpenMs))
	}
	if cfg.HookSignRequired != nil {
		dst = protowire.AppendTag(dst, 12, protowire.VarintType)
		if *cfg.HookSignRequired {
			dst = protowire.AppendVarint(dst, 1)
		} else {
			dst = protowire.AppendVarint(dst, 0)
		}
	}

	return dst
}

// ---- AuthResponse (meta=1, ok=2, user_id=3, device_id=4, config=5, reason=6) ----
func EncodeAuthResponse(resp AuthResponse) []byte {
	var out []byte
	out = protowire.AppendTag(out, 1, protowire.BytesType)
	out = protowire.AppendBytes(out, appendHookMeta(nil, resp.Meta))
	if resp.Ok {
		out = protowire.AppendTag(out, 2, protowire.VarintType)
		out = protowire.AppendVarint(out, 1)
	}
	if resp.UserID != "" {
		out = protowire.AppendTag(out, 3, protowire.BytesType)
		out = protowire.AppendString(out, resp.UserID)
	}
	if resp.DeviceID != "" {
		out = protowire.AppendTag(out, 4, protowire.BytesType)
		out = protowire.AppendString(out, resp.DeviceID)
	}
	if resp.Config != nil {
		out = protowire.AppendTag(out, 5, protowire.BytesType)
		out = protowire.AppendBytes(out, appendTenantRuntimeConfig(nil, *resp.Config))
	}
	if resp.Reason != "" {
		out = protowire.AppendTag(out, 6, protowire.BytesType)
		out = protowire.AppendString(out, resp.Reason)
	}
	return out
}

// ---- CheckMessageResponse (meta=1, allow=2, reason=3) ----
func EncodeCheckMessageResponse(resp CheckMessageResponse) []byte {
	var out []byte
	out = protowire.AppendTag(out, 1, protowire.BytesType)
	out = protowire.AppendBytes(out, appendHookMeta(nil, resp.Meta))
	if resp.Allow {
		out = protowire.AppendTag(out, 2, protowire.VarintType)
		out = protowire.AppendVarint(out, 1)
	}
	if resp.Reason != "" {
		out = protowire.AppendTag(out, 3, protowire.BytesType)
		out = protowire.AppendString(out, resp.Reason)
	}
	return out
}

// ---- GetGroupMembersResponse (meta=1, user_ids=2 repeated string) ----
func EncodeGetGroupMembersResponse(resp GetGroupMembersResponse) []byte {
	var out []byte
	out = protowire.AppendTag(out, 1, protowire.BytesType)
	out = protowire.AppendBytes(out, appendHookMeta(nil, resp.Meta))
	for _, u := range resp.UserIDs {
		out = protowire.AppendTag(out, 2, protowire.BytesType)
		out = protowire.AppendString(out, u)
	}
	return out
}

