# MICS SDK Backlog (P1) — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Close V1 SDK gaps from `docs/MICS（极简IM通讯服务）需求文档V1.0.md` section 4.6 with server-side hook SDKs first.

**Principles:**
- SDKs live under `sdk/<language>/...`
- Protobuf-only payloads (`application/protobuf`), source-generated code where applicable
- Multi-tenant isolation helpers are mandatory (TenantId in every call, topic/key naming helpers)

---

## P1-1: Hook SDK — Java (Spring Boot)

**Status:** Done ✅

**Files:**
- Implemented: `sdk/java/mics-hook-sdk/` (Maven multi-module under `sdk/java/`)
- Implemented: `sdk/java/samples/hook-server/` (JDK `HttpServer` sample)
- Implemented: `sdk/java/samples/spring-hook-server/` (Spring Boot sample)
- Implemented: `sdk/java/samples/kafka-consumer/`

**Steps:**
1. Add proto compilation (protobuf-maven-plugin) and generate `mics_hook.proto` / `mics_message.proto`
2. Implement HTTP hook helpers (Protobuf decode/encode + HMAC signature verify)
3. Add Kafka consumer sample for `im-mics-{TenantId}-event`
4. Add docs + runnable sample configuration

---

## P1-2: Hook SDK — Go

**Status:** Done ✅

**Files:**
- Implemented: `sdk/go/mics-hook-sdk/`
- Implemented: `sdk/go/samples/hook-server/`
- Implemented: `sdk/go/samples/kafka-consumer/`

---

## P1-3: Hook SDK — Node.js

**Status:** Done ✅ (ESM-only)

**Files:**
- Implemented: `sdk/node/mics-hook-sdk/`
- Implemented: `sdk/node/mics-hook-sdk/samples/hook-server/`
- Implemented: `sdk/node/mics-hook-sdk/samples/kafka-consumer/`

---

## P1-4: Client SDK roadmap (scope only)

**Status:** In progress

**Implemented:**
- Flutter: `sdk/flutter/mics_client_sdk/` ✅

**Next candidates (order):**
1. WeChat Mini Program ✅ (`sdk/wechat/mics-client-sdk/`)
2. Android (Kotlin) ✅ (`sdk/android/mics-client-sdk/`)
3. iOS (Swift)

**Minimum parity:**
- connect/reconnect/heartbeat
- send + ACK tracking + retry rules (no retry on `FAILED` ack)
- optional message body encryption (client-side only)
