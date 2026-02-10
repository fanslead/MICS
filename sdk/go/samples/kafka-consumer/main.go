package main

import (
	"context"
	"log"
	"os"
	"strings"

	"github.com/mics-im/mics-hook-sdk-go/micshooksdk"
	"github.com/segmentio/kafka-go"
)

func main() {
	bootstrap := getenv("KAFKA_BOOTSTRAP_SERVERS", "localhost:9092")
	tenantID := getenv("TENANT_ID", "t1")
	tenantSecret := getenv("TENANT_SECRET", "")
	requireSign := strings.EqualFold(getenv("REQUIRE_SIGN", "false"), "true")

	groupID := getenv("KAFKA_GROUP_ID", "mics-hook-"+tenantID)
	topic := getenv("KAFKA_TOPIC", micshooksdk.EventTopicName(tenantID))

	reader := kafka.NewReader(kafka.ReaderConfig{
		Brokers: strings.Split(bootstrap, ","),
		GroupID: groupID,
		Topic:   topic,
	})
	defer reader.Close()

	log.Printf("Kafka consumer started brokers=%s groupId=%s topic=%s requireSign=%v", bootstrap, groupID, topic, requireSign)

	ctx := context.Background()
	for {
		m, err := reader.ReadMessage(ctx)
		if err != nil {
			log.Fatalf("read message: %v", err)
		}

		res, err := micshooksdk.DecodeAndVerifyMqEvent(m.Value, tenantSecret, requireSign)
		if err != nil {
			log.Printf("decode error: %v", err)
			continue
		}
		if !res.Ok {
			log.Printf("drop event: %s tenant=%s type=%d msg=%s", res.Reason, res.Event.TenantID, res.Event.EventType, res.Event.MsgID)
			continue
		}

		log.Printf("event tenant=%s type=%d msg=%s user=%s device=%s node=%s trace=%s", res.Event.TenantID, res.Event.EventType, res.Event.MsgID, res.Event.UserID, res.Event.DeviceID, res.Event.NodeID, res.Event.TraceID)
	}
}

func getenv(key, fallback string) string {
	if v := os.Getenv(key); v != "" {
		return v
	}
	return fallback
}

