package micshooksdk

type MqVerifyResult struct {
	Ok     bool
	Reason string
	Event  MqEvent
}

func EventTopicName(tenantID string) string {
	return "im-mics-" + tenantID + "-event"
}

func DlqTopicName(tenantID string) string {
	return "im-mics-" + tenantID + "-event-dlq"
}

func DecodeMqEvent(b []byte) (MqEvent, error) {
	return decodeMqEvent(b)
}

func EncodeMqEvent(evt MqEvent) []byte {
	return encodeMqEvent(evt)
}

func DecodeAndVerifyMqEvent(b []byte, tenantSecret string, requireSign bool) (MqVerifyResult, error) {
	evt, err := decodeMqEvent(b)
	if err != nil {
		return MqVerifyResult{}, err
	}

	if !requireSign && evt.Sign == "" {
		return MqVerifyResult{Ok: true, Reason: "", Event: evt}, nil
	}
	if evt.Sign == "" {
		return MqVerifyResult{Ok: false, Reason: "invalid sign", Event: evt}, nil
	}

	payloadForSign := encodeMqEvent(clearMqSign(evt))
	if !VerifyMqEventSignBase64(tenantSecret, payloadForSign, evt.Sign) {
		return MqVerifyResult{Ok: false, Reason: "invalid sign", Event: evt}, nil
	}

	return MqVerifyResult{Ok: true, Reason: "", Event: evt}, nil
}

func clearMqSign(evt MqEvent) MqEvent {
	evt.Sign = ""
	return evt
}

