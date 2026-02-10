package com.mics.samples.kafkaconsumer;

import com.mics.contracts.hook.v1.MqEvent;
import com.mics.hooksdk.MqEventDecoder;
import com.mics.hooksdk.MqEventSigner;
import org.apache.kafka.clients.consumer.ConsumerConfig;
import org.apache.kafka.clients.consumer.ConsumerRecord;
import org.apache.kafka.clients.consumer.ConsumerRecords;
import org.apache.kafka.clients.consumer.KafkaConsumer;
import org.apache.kafka.common.serialization.ByteArrayDeserializer;
import org.apache.kafka.common.serialization.StringDeserializer;

import java.time.Duration;
import java.util.List;
import java.util.Properties;
import java.util.UUID;
import java.util.concurrent.atomic.AtomicBoolean;

public final class Main {
    public static void main(String[] args) {
        String tenantId = env("TENANT_ID", "").trim();
        if (tenantId.isBlank()) {
            System.err.println("TENANT_ID is required");
            System.exit(2);
        }

        String tenantSecret = env("TENANT_SECRET", "").trim();
        boolean requireSign = envBool("REQUIRE_SIGN", false);

        String brokers = env("KAFKA_BROKERS", "localhost:9092");
        String topic = env("TOPIC", "im-mics-" + tenantId + "-event");
        String groupId = env("GROUP_ID", "mics-hook-sample-" + UUID.randomUUID());

        Properties props = new Properties();
        props.put(ConsumerConfig.BOOTSTRAP_SERVERS_CONFIG, brokers);
        props.put(ConsumerConfig.GROUP_ID_CONFIG, groupId);
        props.put(ConsumerConfig.ENABLE_AUTO_COMMIT_CONFIG, "true");
        props.put(ConsumerConfig.AUTO_OFFSET_RESET_CONFIG, "latest");
        props.put(ConsumerConfig.KEY_DESERIALIZER_CLASS_CONFIG, StringDeserializer.class.getName());
        props.put(ConsumerConfig.VALUE_DESERIALIZER_CLASS_CONFIG, ByteArrayDeserializer.class.getName());

        AtomicBoolean running = new AtomicBoolean(true);
        Runtime.getRuntime().addShutdownHook(new Thread(() -> running.set(false)));

        System.out.println("Consuming topic=" + topic + " brokers=" + brokers + " requireSign=" + requireSign);
        try (KafkaConsumer<String, byte[]> consumer = new KafkaConsumer<>(props)) {
            consumer.subscribe(List.of(topic));
            while (running.get()) {
                ConsumerRecords<String, byte[]> records = consumer.poll(Duration.ofMillis(500));
                for (ConsumerRecord<String, byte[]> r : records) {
                    handleRecord(r, tenantSecret, requireSign);
                }
            }
        }
        System.out.println("Stopped.");
    }

    private static void handleRecord(ConsumerRecord<String, byte[]> record, String tenantSecret, boolean requireSign) {
        MqEvent evt;
        try {
            evt = MqEvent.parseFrom(record.value());
        } catch (Exception e) {
            System.err.println("invalid protobuf payload at offset=" + record.offset());
            return;
        }

        if (!tenantSecret.isBlank()) {
            boolean ok = MqEventSigner.verify(tenantSecret, evt, requireSign);
            if (!ok) {
                System.err.println("invalid sign tenant=" + evt.getTenantId() + " type=" + evt.getEventType());
                return;
            }
        } else if (requireSign) {
            System.err.println("REQUIRE_SIGN=true but TENANT_SECRET is empty, skipping verification");
        }

        String prefix = "tenant=" + evt.getTenantId() + " type=" + evt.getEventType() + " traceId=" + evt.getTraceId();
        MqEventDecoder.tryDecodeConnectAck(evt).ifPresentOrElse(
                ack -> System.out.println(prefix + " connect user=" + ack.getUserId() + " device=" + ack.getDeviceId() + " node=" + ack.getNodeId()),
                () -> MqEventDecoder.tryDecodeMessage(evt).ifPresentOrElse(
                        msg -> System.out.println(prefix + " msgId=" + msg.getMsgId() + " from=" + msg.getUserId() + " to=" + msg.getToUserId() + " group=" + msg.getGroupId() + " bytes=" + msg.getMsgBody().size()),
                        () -> System.out.println(prefix + " (event_data decode skipped)")
                )
        );
    }

    private static String env(String key, String defaultValue) {
        String v = System.getenv(key);
        return (v == null) ? defaultValue : v;
    }

    private static boolean envBool(String key, boolean defaultValue) {
        String s = System.getenv(key);
        if (s == null || s.isBlank()) {
            return defaultValue;
        }
        return "1".equals(s.trim()) || "true".equalsIgnoreCase(s.trim()) || "yes".equalsIgnoreCase(s.trim());
    }
}

