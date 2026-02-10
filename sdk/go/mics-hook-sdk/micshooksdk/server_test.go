package micshooksdk

import (
	"bytes"
	"context"
	"net/http"
	"net/http/httptest"
	"testing"
)

func TestHookServer_Auth_VerifiesSign_WhenRequired(t *testing.T) {
	secretProvider := func(tenantID string) (string, bool) {
		if tenantID == "t1" {
			return "secret", true
		}
		return "", false
	}

	srv := NewHookServer(HookServerOptions{
		RequireSign:        true,
		TenantSecretLookup: secretProvider,
		Auth: func(ctx context.Context, req AuthRequest) (AuthResponse, error) {
			return AuthResponse{
				Meta:     req.Meta,
				Ok:       true,
				UserID:   "u1",
				DeviceID: req.DeviceID,
				Config: &TenantRuntimeConfig{
					HookBaseURL:             "http://hook",
					HeartbeatTimeoutSeconds: 30,
					OfflineBufferTTLSeconds: 300,
					TenantSecret:            "secret",
				},
			}, nil
		},
	})

	meta := HookMeta{TenantID: "t1", RequestID: "r1", TimestampMs: 123, Sign: "", TraceID: "tr1"}
	req := AuthRequest{Meta: meta, Token: "valid:u1", DeviceID: "d1"}

	// Sign rule: HMAC( Encode(req with meta.sign cleared) + requestId + timestampLE64 )
	payloadForSign := EncodeAuthRequest(AuthRequest{Meta: HookMeta{TenantID: meta.TenantID, RequestID: meta.RequestID, TimestampMs: meta.TimestampMs, Sign: "", TraceID: meta.TraceID}, Token: req.Token, DeviceID: req.DeviceID})
	sign := ComputeHookSignBase64("secret", payloadForSign, meta.RequestID, meta.TimestampMs)
	req.Meta.Sign = sign

	body := EncodeAuthRequest(req)

	r := httptest.NewRequest(http.MethodPost, "/auth", bytes.NewReader(body))
	r.Header.Set("Content-Type", "application/protobuf")
	w := httptest.NewRecorder()

	srv.ServeHTTP(w, r)
	if w.Code != 200 {
		t.Fatalf("unexpected status: %d body=%q", w.Code, w.Body.String())
	}
	if ct := w.Header().Get("Content-Type"); ct != "application/protobuf" {
		t.Fatalf("unexpected content-type: %q", ct)
	}
	if len(w.Body.Bytes()) == 0 {
		t.Fatalf("empty response body")
	}
}
