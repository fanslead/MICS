import { Kafka } from "kafkajs";
import { decodeAndVerifyMqEvent, eventTopicName } from "../../dist/index.js";

const bootstrapServers = process.env.KAFKA_BOOTSTRAP_SERVERS ?? "localhost:9092";
const tenantId = process.env.TENANT_ID ?? "t1";
const tenantSecret = process.env.TENANT_SECRET ?? "";
const requireSign = (process.env.REQUIRE_SIGN ?? "false").toLowerCase() === "true";

const groupId = process.env.KAFKA_GROUP_ID ?? `mics-hook-${tenantId}`;
const topic = process.env.KAFKA_TOPIC ?? eventTopicName(tenantId);

const kafka = new Kafka({
  clientId: process.env.KAFKA_CLIENT_ID ?? "mics-hook-sample",
  brokers: bootstrapServers.split(",").map((s) => s.trim()).filter(Boolean),
});

const consumer = kafka.consumer({ groupId });

await consumer.connect();
await consumer.subscribe({ topic, fromBeginning: false });

// eslint-disable-next-line no-console
console.log(`Kafka consumer started. brokers=${bootstrapServers} groupId=${groupId} topic=${topic} requireSign=${requireSign}`);

await consumer.run({
  eachMessage: async ({ message }) => {
    if (!message.value) return;
    const bytes = new Uint8Array(message.value);

    const { ok, event, reason } = decodeAndVerifyMqEvent(bytes, { tenantSecret, requireSign });
    if (!ok) {
      // eslint-disable-next-line no-console
      console.warn(`drop event: ${reason} tenant=${event.tenantId} type=${event.eventType} msg=${event.msgId}`);
      return;
    }

    // eslint-disable-next-line no-console
    console.log(
      `event tenant=${event.tenantId} type=${event.eventType} msg=${event.msgId} user=${event.userId} device=${event.deviceId} node=${event.nodeId} trace=${event.traceId}`
    );
  },
});

