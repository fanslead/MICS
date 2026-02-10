# M3 Offline Message Optimization (Plan A / Best-Effort) — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Implement “方案三：Kafka 异步通知 + Hook 拉取” as an *opt-in per-tenant best-effort path* that reduces gateway memory pressure and aligns with “纯通讯管道 / Hook 外置 / 无状态集群 / 多租户强隔离”.

**Non-goal:** Guarantee “离线消息 100% 不丢失”. Reliability is owned by the business side (Kafka consumer + storage + idempotency). The gateway remains best-effort and must degrade safely without blocking real-time routing/forwarding.

**Key constraints (from requirements):**
- No business data storage in gateway (only short-lived in-memory state allowed for degradation).
- Per-tenant isolation: MQ Topic / Redis keys / rate limits / hook policies and failure must not impact other tenants.
- AOT-friendly: Protobuf source-gen, no reflection/dynamic code paths.

---

## Task 1: Align contracts (append-only, backward compatible)

**Files:**
- Modify: `src/Mics.Contracts/Protos/mics_hook.proto`
- Modify: `sdk/go/mics-hook-sdk/proto/Protos/mics_hook.proto`
- Modify: `sdk/java/mics-hook-sdk/src/main/proto/Protos/mics_hook.proto`
- (If exists) Test: `tests/Mics.Tests/MqEventProtoTests.cs`

**Step 1: Add offline MQ event type**
- Append `OFFLINE_MESSAGE` to `enum EventType` (do not renumber existing values).

**Step 2: Add Hook pull API (protobuf payload)**
- Add:
  - `GetOfflineMessagesRequest { HookMeta meta; string user_id; optional string device_id; int32 max_messages; string cursor; }`
  - `GetOfflineMessagesResponse { HookMeta meta; bool ok; repeated mics.message.v1.MessageRequest messages; string reason; string next_cursor; bool has_more; }`
- Add a new per-tenant feature switch to `TenantRuntimeConfig` (append-only), e.g. `optional bool offline_use_hook_pull = <next_field_id>;`.

**Step 3: Update SDK proto mirrors**
- Keep fields and numbers exactly identical across gateway/contracts and SDKs.

**Step 4: Proto compatibility test**
- Add/extend a test to assert the proto contains:
  - `OFFLINE_MESSAGE`
  - `GetOfflineMessagesRequest/Response`
  - `offline_use_hook_pull`

---

## Task 2: Produce `OFFLINE_MESSAGE` events only on offline path

**Files:**
- Modify: `src/Mics.Gateway/Mq/MqEventFactory.cs`
- Modify: `src/Mics.Gateway/Ws/WsGatewayHandler.cs`
- Test: `tests/Mics.Tests/MqEventFactoryTests.cs`

**Step 1: Add a factory method**
- Add `MqEventFactory.CreateOfflineMessage(...)` that sets:
  - `EventType = EventType.OfflineMessage`
  - `ToUserId = <offline recipient>`
  - `EventData = MessageRequest bytes`
  - `MsgId/UserId/DeviceId/TenantId/NodeId/TraceId/Timestamp` populated like existing message events
  - `Sign` computed with existing “clear sign then HMAC over bytes” rule.

**Step 2: Wire it at the correct decision point**
- In single chat:
  - When `routes.Count == 0`: enqueue `OFFLINE_MESSAGE` and *do not* store in local offline buffer for hook-enabled tenants.
  - When cross-node forward fails: enqueue `OFFLINE_MESSAGE` (recipient known) before any fallback.
- Keep the existing local offline buffer as **fallback only** when MQ enqueue fails or tenant is not hook-enabled.

**Step 3: Add tests**
- Validate `CreateOfflineMessage` produces a correctly signed event and sets `ToUserId`.

---

## Task 3: Add `/get-offline-messages` to HookClient (best-effort, non-blocking)

**Files:**
- Modify: `src/Mics.Gateway/Hook/HookClient.cs`
- Modify: `src/Mics.Gateway/Hook/HookOperation.cs` (or equivalent op enum if present)
- Test: `tests/Mics.Tests/*Hook*.cs` (new/extend)

**Step 1: Extend interface**
- Add `GetOfflineMessagesAsync(...)` to `IHookClient`.

**Step 2: Implement with existing policies**
- Reuse the existing pattern:
  - per-tenant policy resolve
  - concurrency limiter acquire
  - circuit breaker begin/end + success/failure tracking
  - signing with `tenant_secret` when available (honor sign-required policy)
  - record metrics and structured logs with `tenant/op/result`
- Degrade behavior:
  - If circuit open / queue rejected / timeout / network: return `Ok=false` with `Degraded=true` and empty list.
  - Must not block WebSocket connect (drain is “nice-to-have”).

**Step 3: Tests**
- At minimum: validate “sign required + missing secret” fails closed (or returns non-degraded failure) consistently with other hook ops.

---

## Task 4: Drain offline via Hook on connect (opt-in per tenant)

**Files:**
- Modify: `src/Mics.Gateway/Ws/WsGatewayHandler.cs`
- Test: `tests/Mics.Tests/*Offline*.cs` (new/extend)
- Modify: `docs/ops/acceptance.md`

**Step 1: Gate on tenant config**
- If `tenantCfg.offline_use_hook_pull == true`:
  - Call hook in a bounded loop: `max_messages=100`, `max_pages=10` (cap total to 1000).
  - Push messages to the client as `ServerFrame.delivery`.
  - Then drain the **local fallback** offline buffer store (in case MQ enqueue failed earlier).
- Else:
  - Keep the current local drain behavior unchanged.

**Step 2: Observability**
- Add metrics:
  - `mics_hook_get_offline_messages_total{tenant,result}`
  - `mics_offline_drained_from_hook_total{tenant}`
  - `mics_offline_drain_failed_total{tenant}` (if not already present)
- Ensure logs include `TenantId/TraceId/UserId` on failures (rate-limited).

**Step 3: Update acceptance checklist**
- Add an acceptance section for “offline pull enabled tenant” covering:
  - Kafka event published count vs. hook drained count
  - hook timeout/degraded does not block connect

---

## Task 5: MQ queue isolation (prevent cross-tenant impact)

**Files:**
- Modify: `src/Mics.Gateway/Mq/MqEventDispatcher.cs`
- Modify: `src/Mics.Gateway/Mq/MqEventDispatcherOptions.cs`
- Modify: `src/Mics.Gateway/Composition/GatewayServiceProvider.cs`
- Test: `tests/Mics.Tests/*Mq*.cs` (new/extend)

**Step 1: Isolate by tenant**
- Replace the single global bounded channel with a per-tenant bounded channel map OR enforce per-tenant quotas at enqueue time.
- Ensure queue-full behavior is visible (`mics_mq_dropped_total{tenant,reason="queue_full"}`) and does not starve other tenants.

**Step 2: Keep best-effort semantics**
- Never block the message processing hot path.
- When enqueue fails for `OFFLINE_MESSAGE`, fall back to short-term local buffer store (TTL) and record metrics with `via="local_fallback"`.

---

## Task 6: Rollout plan (safe migration)

**Step 1: Opt-in only**
- Default `offline_use_hook_pull=false` until the business side is ready.

**Step 2: Canary tenants**
- Enable the flag for small tenants first; observe:
  - MQ publish/dropped/DLQ rates
  - hook latency and degraded counts
  - offline drained counts and connect latency impact

**Step 3: Expand gradually**
- Increase tenants and traffic; keep a rollback lever:
  - flip the tenant flag off (revert to local buffer)

**Verification command (when implementing):**
- `dotnet test Mics.slnx -c Release`

