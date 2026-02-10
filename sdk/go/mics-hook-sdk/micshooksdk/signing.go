package micshooksdk

import (
	"crypto/hmac"
	"crypto/sha256"
	"encoding/base64"
	"encoding/binary"
)

func ComputeHookSignBase64(tenantSecret string, payloadBytes []byte, requestID string, timestampMs int64) string {
	var tsBytes [8]byte
	binary.LittleEndian.PutUint64(tsBytes[:], uint64(timestampMs))

	mac := hmac.New(sha256.New, []byte(tenantSecret))
	mac.Write(payloadBytes)
	mac.Write([]byte(requestID))
	mac.Write(tsBytes[:])
	return base64.StdEncoding.EncodeToString(mac.Sum(nil))
}

func VerifyHookSignBase64(tenantSecret string, payloadBytes []byte, requestID string, timestampMs int64, signBase64 string) bool {
	if signBase64 == "" {
		return false
	}
	if !isCanonicalBase64(signBase64) {
		return false
	}

	expected := ComputeHookSignBase64(tenantSecret, payloadBytes, requestID, timestampMs)
	return constantTimeEqualBase64(expected, signBase64)
}

func ComputeMqEventSignBase64(tenantSecret string, payloadBytes []byte) string {
	mac := hmac.New(sha256.New, []byte(tenantSecret))
	mac.Write(payloadBytes)
	return base64.StdEncoding.EncodeToString(mac.Sum(nil))
}

func VerifyMqEventSignBase64(tenantSecret string, payloadBytes []byte, signBase64 string) bool {
	if signBase64 == "" {
		return false
	}
	if !isCanonicalBase64(signBase64) {
		return false
	}
	expected := ComputeMqEventSignBase64(tenantSecret, payloadBytes)
	return constantTimeEqualBase64(expected, signBase64)
}

func constantTimeEqualBase64(a, b string) bool {
	ab, err := base64.StdEncoding.DecodeString(a)
	if err != nil {
		return false
	}
	bb, err := base64.StdEncoding.DecodeString(b)
	if err != nil {
		return false
	}
	if len(ab) != len(bb) {
		return false
	}
	return hmac.Equal(ab, bb)
}

func isCanonicalBase64(s string) bool {
	decoded, err := base64.StdEncoding.DecodeString(s)
	if err != nil {
		return false
	}
	return base64.StdEncoding.EncodeToString(decoded) == s
}

