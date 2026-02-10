# M2-1 Signing Hardening Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Enforce and document consistent HMAC signing for HTTP Hook requests and MQ Hook events, aligning with `docs/MICS（极简IM通讯服务）需求文档V1.0.md` security requirements.

**Architecture:** Keep signing logic deterministic and AOT-friendly by using HMAC-SHA256 over Protobuf bytes. For HTTP Hook, reuse existing meta-based signing (payload + requestId + timestamp). For MQ Hook, add an explicit `sign` field to `MqEvent` and sign the event bytes (with `sign` cleared).

**Tech Stack:** .NET 10, Google.Protobuf, HMACSHA256, existing Gateway hook policy + circuit breaker.

---

### Task 1: Add `MqEvent.sign` field to the Protobuf contract

**Files:**
- Modify: `src/Mics.Contracts/Protos/mics_hook.proto`
- Test: `tests/Mics.Tests/MqEventProtoTests.cs`

**Step 1: Update proto**
- Add `string sign = 11;` to `message MqEvent` (append-only, backward compatible).

**Step 2: Update proto tests**
- Assert `mics_hook.proto` contains `string sign = 11;`.

**Step 3: Run tests**
- Run: `dotnet test Mics.slnx -c Release`

---

### Task 2: Add an HMAC helper for MQ event signing

**Files:**
- Modify: `src/Mics.Gateway/Security/HmacSign.cs`
- Test: `tests/Mics.Tests/MqEventFactoryTests.cs`

**Step 1: Write failing test**
- Extend `MqEventFactoryTests` to validate `evt.Sign` equals `HMACSHA256(secret, Serialize(evt with sign cleared))` (Base64).

**Step 2: Implement minimal helper**
- Add `HmacSign.ComputeBase64(string tenantSecret, IMessage payloadForSign)` overload that signs the Protobuf bytes only.

**Step 3: Run tests**
- Run: `dotnet test tests/Mics.Tests/Mics.Tests.csproj -c Release`

---

### Task 3: Sign MQ events in `MqEventFactory` and wire secrets from sessions

**Files:**
- Modify: `src/Mics.Gateway/Mq/MqEventFactory.cs`
- Modify: `src/Mics.Gateway/Ws/WsGatewayHandler.cs`
- Test: `tests/Mics.Tests/MqEventFactoryTests.cs`

**Step 1: Update factory API**
- Add `tenantSecret` parameter to:
  - `CreateConnectOnline(...)`
  - `CreateConnectOffline(...)`
  - `CreateForMessage(...)`
- Set `evt.Sign` when `tenantSecret` is non-empty (otherwise empty string).

**Step 2: Update WS caller**
- Pass `session.TenantConfig.TenantSecret` into MQ event factory calls.

**Step 3: Run tests**
- Run: `dotnet test Mics.slnx -c Release`

---

### Task 4: Enforce “sign required” semantics for HTTP hooks and improve failure reasons

**Files:**
- Modify: `src/Mics.Gateway/Hook/HookClient.cs`
- Modify: `src/Mics.Gateway/Ws/WsGatewayHandler.cs`
- Test: `tests/Mics.Tests/HookSigningTests.cs`

**Step 1: Add failing test**
- Add a test that when `HookSignRequired=true` and `TenantRuntimeConfig.TenantSecret` is empty:
  - `CheckMessageAsync` returns `Allow=false` with a reason mentioning signature requirement.

**Step 2: Implement minimal code**
- Change `CheckMessageAsync` to fail closed (deny) when signature is required but `tenant_secret` is missing.
- (Optional) Extend `GroupMembersResult` to include a reason string and surface it in the WS ACK reason path.

**Step 3: Run tests**
- Run: `dotnet test tests/Mics.Tests/Mics.Tests.csproj -c Release`

---

### Task 5: Enforce signing in HookMock when configured

**Files:**
- Modify: `src/Mics.HookMock/Program.cs`

**Step 1: Require sign when configured**
- If `HOOK_SIGN_REQUIRED=true` (or tenant override), reject requests with empty `meta.sign`.
- Continue validating signature when provided.

**Step 2: Quick manual verification**
- Start HookMock with `HOOK_SIGN_REQUIRED=true` and call endpoints:
  - Unsigned Protobuf requests should fail.
  - Signed requests should pass.

---

### Task 6: Update roadmap

**Files:**
- Modify: `docs/plans/2026-02-08-mics-v1-roadmap.md`

**Step 1: Mark M2-1 delivered**
- Note: HTTP Hook signing enforcement + MQ event signing field and generation.

