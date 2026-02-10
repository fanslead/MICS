package micshooksdk

import (
	"crypto/hmac"
	"crypto/sha256"
	"encoding/base64"
	"encoding/binary"
	"testing"
)

func TestComputeHookSignBase64_MatchesSpec(t *testing.T) {
	tenantSecret := "secret"
	payload := []byte{0x0a, 0x01, 0x01} // arbitrary
	requestID := "r1"
	ts := int64(123)

	got := ComputeHookSignBase64(tenantSecret, payload, requestID, ts)

	var tsBytes [8]byte
	binary.LittleEndian.PutUint64(tsBytes[:], uint64(ts))
	mac := hmac.New(sha256.New, []byte(tenantSecret))
	mac.Write(payload)
	mac.Write([]byte(requestID))
	mac.Write(tsBytes[:])
	want := base64.StdEncoding.EncodeToString(mac.Sum(nil))

	if got != want {
		t.Fatalf("unexpected sign: got=%q want=%q", got, want)
	}
}

func TestVerifyHookSignBase64_RejectsNonCanonicalBase64(t *testing.T) {
	// "YQ==" is base64("a"). Appending "x" should be rejected.
	if VerifyHookSignBase64("secret", []byte("p"), "r", 1, "YQ==x") {
		t.Fatalf("expected non-canonical base64 to be rejected")
	}
}

