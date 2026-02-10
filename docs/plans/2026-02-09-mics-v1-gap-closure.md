# MICS V1 Gap Closure Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Close remaining V1 doc gaps with deployment/ops artifacts, acceptance playbooks, and spec alignment.

**Architecture:** Keep MICS as a pure comms pipe (no business storage) while improving operability: K8s manifests, Prometheus/alerts examples, and doc/spec consistency. Any business logic remains external via HTTP/MQ hooks.

**Tech Stack:** .NET 10 NativeAOT, ASP.NET Core WebSocket, gRPC/HTTP2, Protobuf (source-gen), Redis, Kafka, Prometheus/Grafana (examples), Kubernetes.

---

## Priority (P0 → P2)

### P0 — Blocks acceptance / delivery
- Deployment & ops deliverables (K8s + HPA + monitoring scrape + sample alerts + ingress guidance)
- Acceptance playbooks (chapter 8 test steps; load tool usage; failure drills)
- Spec alignment for known contradictions (Hook payloads, hook gating semantics, event model)

### P1 — Improves integration & ecosystem
- Hook SDKs for Java/Go/Node (server-side integration priority)
- Client SDKs beyond TS(Web) and .NET (Flutter/miniapp/Android/iOS)

### P2 — Hardening & polish
- Security hardening knobs and recommended LB/WAF config
- More metrics (latency histograms, tenant dashboards), runbook expansion

---

## Task 1: Spec alignment (doc vs implementation)

**Files:**
- Modify: `docs/MICS（极简IM通讯服务）需求文档V1.0.md`

**Step 1: Update Hook payload format**
- Change “Protobuf/JSON” to “Protobuf（application/protobuf）” for HTTP hook requests/responses.
- Ensure the doc still states Protobuf forward compatibility rules (only add fields).

**Step 2: Clarify hook gating semantics**
- Update single/group flow sections to state:
  - Route lookup & hook calls may run in parallel for latency
  - Delivery is gated by `/check-message allow` (except explicit degraded fallback policy)

**Step 3: Clarify MQ event model**
- State that “group member change event” is produced by external systems (MICS defines schema but does not own membership).

**Step 4: Verify build/tests**
Run: `dotnet test .\\Mics.slnx -c Release`
Expected: PASS.

---

## Task 2: Kubernetes deployment completeness (examples)

**Files:**
- Create: `deploy/k8s/gateway.configmap.yaml`
- Create: `deploy/k8s/gateway.hpa.yaml`
- Create: `deploy/k8s/gateway.ingress.yaml`
- Modify: `deploy/k8s/gateway.deployment.yaml`
- Modify: `deploy/k8s/gateway.service.yaml`
- Create: `deploy/k8s/README.md`

**Step 1: Add ConfigMap for env**
- Move example env vars (Redis, tenant hook map, kafka optional) into a ConfigMap.

**Step 2: Add resource requests/limits**
- Add requests/limits placeholders to deployment for production baseline.

**Step 3: Add HPA**
- Provide CPU-based HPA (works everywhere).
- Provide optional connections-based HPA section (requires Prometheus adapter); document prerequisites.

**Step 4: Add Ingress (WebSocket)**
- Provide nginx ingress example for `/ws` with WebSocket annotations.
- Explicitly document that gRPC is internal (cluster-only) and should not go through the WS ingress.

---

## Task 3: Monitoring & alerting examples (Prometheus/Grafana/Alertmanager)

**Files:**
- Create: `deploy/k8s/monitoring/gateway-servicemonitor.yaml`
- Create: `deploy/k8s/monitoring/gateway-prometheusrule.yaml`
- Create: `docs/ops/monitoring.md`

**Step 1: Add ServiceMonitor**
- Scrape `/metrics` on gateway service (Prometheus Operator example).

**Step 2: Add sample alert rules**
- CPU high, hook failure/timeout spike, grpc forward failure spike, WS connection drop spike.

**Step 3: Document dashboards/alerts**
- Explain tenant dimensions (`tenant` label) and how to build panels.
- State that delivery channels (email/sms/im bots) are configured in Alertmanager (external).

---

## Task 4: Acceptance playbook (Chapter 8 runnable steps)

**Files:**
- Create: `docs/ops/acceptance.md`

**Step 1: Functional acceptance**
- Connection errors (4001/4002), heartbeat timeout (4100), rate limits (4429), protobuf error (4400).

**Step 2: Cluster acceptance**
- Two gateways, verify cross-node gRPC forwarding and dead-node cleanup behavior.

**Step 3: Hook degradation acceptance**
- Force hook timeout and verify degrade policy/metrics/logs.

**Step 4: Performance/soak entry points**
- Provide `tools/Mics.LoadTester` command templates and what to record.

---

## Task 5: P1 SDK backlog (scoped plan only)

**Files:**
- Create: `docs/plans/2026-02-09-mics-sdk-backlog.md`

**Step 1: Define Hook SDK API surface parity**
- Java/Go/Node: Protobuf HTTP decoding, signature verify, sample server, Kafka consumer example.

**Step 2: Define client SDK roadmap**
- Flutter/miniapp/Android/iOS: feature parity and minimum demos.

