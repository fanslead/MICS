package micshooksdk

import "testing"

func TestMqTopicNames(t *testing.T) {
	if got := EventTopicName("t1"); got != "im-mics-t1-event" {
		t.Fatalf("unexpected event topic: %q", got)
	}
	if got := DlqTopicName("t1"); got != "im-mics-t1-event-dlq" {
		t.Fatalf("unexpected dlq topic: %q", got)
	}
}

func TestDecodeAndVerifyMqEvent_SignRequired(t *testing.T) {
	evt := MqEvent{
		TenantID:  "t1",
		EventType: EventSingleChatMsg,
		MsgID:     "m1",
		UserID:    "u1",
		DeviceID:  "d1",
		ToUserID:  "u2",
		EventData: []byte{1, 2, 3},
		Timestamp: 123,
		NodeID:    "node-1",
		TraceID:   "tr1",
	}

	signPayload := EncodeMqEvent(MqEvent{TenantID: evt.TenantID, EventType: evt.EventType, MsgID: evt.MsgID, UserID: evt.UserID, DeviceID: evt.DeviceID, ToUserID: evt.ToUserID, GroupID: evt.GroupID, EventData: evt.EventData, Timestamp: evt.Timestamp, NodeID: evt.NodeID, Sign: "", TraceID: evt.TraceID})
	evt.Sign = ComputeMqEventSignBase64("secret", signPayload)

	bytes := EncodeMqEvent(evt)
	res, err := DecodeAndVerifyMqEvent(bytes, "secret", true)
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if !res.Ok {
		t.Fatalf("expected ok=true, got reason=%q", res.Reason)
	}
	if res.Event.MsgID != "m1" {
		t.Fatalf("unexpected msg: %q", res.Event.MsgID)
	}
}

