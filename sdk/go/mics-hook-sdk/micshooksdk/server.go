package micshooksdk

import (
	"context"
	"io"
	"net/http"
	"strings"
)

type TenantSecretLookup func(tenantID string) (secret string, ok bool)

type HookServerOptions struct {
	RequireSign        bool
	TenantSecretLookup TenantSecretLookup

	Auth            func(ctx context.Context, req AuthRequest) (AuthResponse, error)
	CheckMessage    func(ctx context.Context, req CheckMessageRequest) (CheckMessageResponse, error)
	GetGroupMembers func(ctx context.Context, req GetGroupMembersRequest) (GetGroupMembersResponse, error)
}

func NewHookServer(options HookServerOptions) http.Handler {
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if r.Method != http.MethodPost {
			w.WriteHeader(http.StatusMethodNotAllowed)
			return
		}

		if ct := r.Header.Get("Content-Type"); ct != "" && !strings.HasPrefix(ct, "application/protobuf") {
			w.WriteHeader(http.StatusUnsupportedMediaType)
			return
		}

		body, err := io.ReadAll(r.Body)
		if err != nil {
			w.WriteHeader(http.StatusBadRequest)
			return
		}

		switch r.URL.Path {
		case "/auth":
			handleAuth(w, r, body, options)
		case "/check-message":
			handleCheckMessage(w, r, body, options)
		case "/get-group-members":
			handleGetGroupMembers(w, r, body, options)
		default:
			w.WriteHeader(http.StatusNotFound)
		}
	})
}

func handleAuth(w http.ResponseWriter, r *http.Request, body []byte, options HookServerOptions) {
	req, err := DecodeAuthRequest(body)
	if err != nil {
		w.WriteHeader(http.StatusBadRequest)
		return
	}

	secret, ok := options.TenantSecretLookup(req.Meta.TenantID)
	if !ok || req.Meta.TenantID == "" {
		writeProtobuf(w, EncodeAuthResponse(AuthResponse{
			Meta:   echoMeta(req.Meta),
			Ok:     false,
			Reason: "unknown tenant",
		}))
		return
	}

	if !verifyHookOrReject(options.RequireSign, secret, req.Meta, EncodeAuthRequest(AuthRequest{Meta: clearSign(req.Meta), Token: req.Token, DeviceID: req.DeviceID})) {
		writeProtobuf(w, EncodeAuthResponse(AuthResponse{
			Meta:   echoMeta(req.Meta),
			Ok:     false,
			Reason: "invalid sign",
		}))
		return
	}

	if options.Auth == nil {
		writeProtobuf(w, EncodeAuthResponse(AuthResponse{
			Meta:   echoMeta(req.Meta),
			Ok:     false,
			Reason: "auth handler not configured",
		}))
		return
	}

	resp, err := options.Auth(r.Context(), req)
	if err != nil {
		writeProtobuf(w, EncodeAuthResponse(AuthResponse{
			Meta:   echoMeta(req.Meta),
			Ok:     false,
			Reason: err.Error(),
		}))
		return
	}
	if resp.Meta.TenantID == "" {
		resp.Meta = echoMeta(req.Meta)
	}

	writeProtobuf(w, EncodeAuthResponse(resp))
}

func handleCheckMessage(w http.ResponseWriter, r *http.Request, body []byte, options HookServerOptions) {
	req, err := DecodeCheckMessageRequest(body)
	if err != nil {
		w.WriteHeader(http.StatusBadRequest)
		return
	}

	secret, ok := options.TenantSecretLookup(req.Meta.TenantID)
	if !ok || req.Meta.TenantID == "" {
		writeProtobuf(w, EncodeCheckMessageResponse(CheckMessageResponse{
			Meta:   echoMeta(req.Meta),
			Allow:  false,
			Reason: "unknown tenant",
		}))
		return
	}

	if !verifyHookOrReject(options.RequireSign, secret, req.Meta, EncodeCheckMessageRequest(CheckMessageRequest{Meta: clearSign(req.Meta), MessageWireBytes: req.MessageWireBytes})) {
		writeProtobuf(w, EncodeCheckMessageResponse(CheckMessageResponse{
			Meta:   echoMeta(req.Meta),
			Allow:  false,
			Reason: "invalid sign",
		}))
		return
	}

	if options.CheckMessage == nil {
		writeProtobuf(w, EncodeCheckMessageResponse(CheckMessageResponse{
			Meta:   echoMeta(req.Meta),
			Allow:  true,
			Reason: "",
		}))
		return
	}

	resp, err := options.CheckMessage(r.Context(), req)
	if err != nil {
		writeProtobuf(w, EncodeCheckMessageResponse(CheckMessageResponse{
			Meta:   echoMeta(req.Meta),
			Allow:  false,
			Reason: err.Error(),
		}))
		return
	}
	if resp.Meta.TenantID == "" {
		resp.Meta = echoMeta(req.Meta)
	}
	writeProtobuf(w, EncodeCheckMessageResponse(resp))
}

func handleGetGroupMembers(w http.ResponseWriter, r *http.Request, body []byte, options HookServerOptions) {
	req, err := DecodeGetGroupMembersRequest(body)
	if err != nil {
		w.WriteHeader(http.StatusBadRequest)
		return
	}

	secret, ok := options.TenantSecretLookup(req.Meta.TenantID)
	if !ok || req.Meta.TenantID == "" {
		writeProtobuf(w, EncodeGetGroupMembersResponse(GetGroupMembersResponse{
			Meta:    echoMeta(req.Meta),
			UserIDs: nil,
		}))
		return
	}

	if !verifyHookOrReject(options.RequireSign, secret, req.Meta, EncodeGetGroupMembersRequest(GetGroupMembersRequest{Meta: clearSign(req.Meta), GroupID: req.GroupID})) {
		writeProtobuf(w, EncodeGetGroupMembersResponse(GetGroupMembersResponse{
			Meta:    echoMeta(req.Meta),
			UserIDs: nil,
		}))
		return
	}

	if options.GetGroupMembers == nil {
		writeProtobuf(w, EncodeGetGroupMembersResponse(GetGroupMembersResponse{
			Meta:    echoMeta(req.Meta),
			UserIDs: nil,
		}))
		return
	}

	resp, err := options.GetGroupMembers(r.Context(), req)
	if err != nil {
		writeProtobuf(w, EncodeGetGroupMembersResponse(GetGroupMembersResponse{
			Meta:    echoMeta(req.Meta),
			UserIDs: nil,
		}))
		return
	}
	if resp.Meta.TenantID == "" {
		resp.Meta = echoMeta(req.Meta)
	}
	writeProtobuf(w, EncodeGetGroupMembersResponse(resp))
}

func verifyHookOrReject(requireSign bool, tenantSecret string, meta HookMeta, payloadForSign []byte) bool {
	if requireSign && meta.Sign == "" {
		return false
	}
	if meta.Sign == "" {
		return true
	}
	return VerifyHookSignBase64(tenantSecret, payloadForSign, meta.RequestID, meta.TimestampMs, meta.Sign)
}

func clearSign(meta HookMeta) HookMeta {
	meta.Sign = ""
	return meta
}

func echoMeta(meta HookMeta) HookMeta {
	// Echo meta as-is for tracing. Keep sign as sent.
	return meta
}

func writeProtobuf(w http.ResponseWriter, payload []byte) {
	w.Header().Set("Content-Type", "application/protobuf")
	w.WriteHeader(http.StatusOK)
	_, _ = w.Write(payload)
}

