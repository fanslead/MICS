package main

import (
	"context"
	"log"
	"net/http"
	"os"
	"strings"

	"github.com/mics-im/mics-hook-sdk-go/micshooksdk"
)

func main() {
	tenantSecret := os.Getenv("TENANT_SECRET")
	if tenantSecret == "" {
		tenantSecret = "dev-secret-t1"
	}

	h := micshooksdk.NewHookServer(micshooksdk.HookServerOptions{
		RequireSign: false,
		TenantSecretLookup: func(tenantID string) (string, bool) {
			if tenantID == "t1" {
				return tenantSecret, true
			}
			return "", false
		},
		Auth: func(ctx context.Context, req micshooksdk.AuthRequest) (micshooksdk.AuthResponse, error) {
			userID := strings.TrimPrefix(req.Token, "valid:")
			if userID == "" {
				userID = "u1"
			}
			return micshooksdk.AuthResponse{
				Meta:     req.Meta,
				Ok:       true,
				UserID:   userID,
				DeviceID: req.DeviceID,
				Config: &micshooksdk.TenantRuntimeConfig{
					HookBaseURL:             "http://localhost:8081",
					HeartbeatTimeoutSeconds: 30,
					OfflineBufferTTLSeconds: 300,
					TenantMaxConnections:    0,
					UserMaxConnections:      0,
					TenantMaxMessageQps:     0,
					TenantSecret:            tenantSecret,
				},
				Reason: "",
			}, nil
		},
		CheckMessage: func(ctx context.Context, req micshooksdk.CheckMessageRequest) (micshooksdk.CheckMessageResponse, error) {
			return micshooksdk.CheckMessageResponse{Meta: req.Meta, Allow: true, Reason: ""}, nil
		},
		GetGroupMembers: func(ctx context.Context, req micshooksdk.GetGroupMembersRequest) (micshooksdk.GetGroupMembersResponse, error) {
			var users []string
			if req.GroupID == "group-1" {
				users = []string{"u1", "u2", "u3"}
			}
			return micshooksdk.GetGroupMembersResponse{Meta: req.Meta, UserIDs: users}, nil
		},
	})

	srv := &http.Server{
		Addr:    ":8081",
		Handler: h,
	}

	log.Printf("Hook server listening on http://localhost:8081")
	log.Fatal(srv.ListenAndServe())
}

