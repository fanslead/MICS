# mics-hook-sdk-go

Go server-side Hook SDK for MICS (Protobuf over HTTP + optional HMAC signature verification).

This SDK is designed to keep business logic **outside** MICS, while making it easy to implement:
- `/auth`
- `/check-message`
- `/get-group-members`
- `/get-offline-messages`

## Status

MVP: typed request/response structs + Protobuf encode/decode (no `protoc` needed) + signature verify helpers.

## Build & Test

Requires Go toolchain.

```bash
go test ./...
```

If module download is blocked in your environment, set a proxy, e.g.:

```bash
export GOPROXY="https://goproxy.cn,direct"
```

## Samples

### Hook server

```bash
cd sdk/go/samples/hook-server
go run .
```

### Kafka consumer

```bash
cd sdk/go/samples/kafka-consumer
go run .
```
