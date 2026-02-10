# @mics/node-hook-sdk (ESM-only)

Node.js server-side Hook SDK for MICS (Protobuf over HTTP + optional HMAC signature verification).

## Build

```bash
npm install
npm run build
```

## Test

```bash
npm test
```

## Sample (minimal http server)

```bash
npm run build
node samples/hook-server/server.mjs
```

## Sample (Kafka consumer)

This sample uses `kafkajs` to consume tenant-scoped MQ events and decodes/verifies `MqEvent` via the SDK.

```bash
npm run build
cd samples/kafka-consumer
npm install
TENANT_ID=t1 TENANT_SECRET=dev-secret-t1 KAFKA_BOOTSTRAP_SERVERS=localhost:9092 npm start
```
