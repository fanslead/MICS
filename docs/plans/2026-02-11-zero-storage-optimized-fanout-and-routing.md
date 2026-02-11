# Zero-Storage Optimized Fanout & Routing (Implementation Notes)

**Goal:** Apply selected improvements from `docs/改进实施指南.md` while keeping “核心服务不持久化业务数据” as the top principle.

**Key decisions (against the requirements doc):**
- **Reject “offline messages persisted to Redis”**: even with TTL, writing `ServerFrame/Message` bytes to Redis is still storage of business message content. MICS may keep only best-effort, in-memory short buffers; durable offline/roaming belongs to external systems via Hook/MQ.
- **Keep routing state in Redis**: online route/lease data is allowed as non-business global state.

## What was implemented

### 1) Group offline path aligned to Hook-pull mode
When `TenantRuntimeConfig.OfflineUseHookPull=true`, group-chat fanout now:
- For offline members (no route): emits `EventType=OfflineMessage` to MQ **per recipient** and skips in-memory buffering when enqueue succeeds.
- For gRPC batch-forward failures: degrades the same way (MQ notify first, in-memory buffer as fallback).

This keeps MICS as a “pipe”: business persistence is external; MICS only notifies + later pulls offline via `/get-offline-messages`.

### 2) Local route cache to reduce Redis load
`OnlineRouteStore.GetAllDevicesAsync` adds an in-process cache (TTL default 5s, size default 100MB), with invalidation on `UpsertAsync/RemoveAsync`.

**Config:**
- `LOCAL_ROUTE_CACHE_TTL_SECONDS` (default `5`, `0` disables)
- `LOCAL_ROUTE_CACHE_SIZE_BYTES` (default `104857600`, `0` disables)

### 3) gRPC forward retries (bounded)
Cross-node forwarding adds:
- `maxRetries=2` for transient `Unavailable/DeadlineExceeded/ResourceExhausted`
- per-attempt deadline `250ms`
- small backoff (`50ms * attempt`)

## Verification
- `dotnet test .\\Mics.slnx -c Release --blame-hang-timeout 2m`

