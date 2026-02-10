# Monitoring & Alerting (MICS Gateway)

This document complements `docs/MICS（极简IM通讯服务）需求文档V1.0.md` section 4.7/7.3/8.4.

## Metrics endpoint

- `GET /metrics` exposes Prometheus text format.
- Recommended scrape interval: 15s.

## Kubernetes (Prometheus Operator) examples

- ServiceMonitor: `deploy/k8s/monitoring/gateway-servicemonitor.yaml`
- PrometheusRule (sample alerts): `deploy/k8s/monitoring/gateway-prometheusrule.yaml`

## Multi-tenant dimensions

Many counters include a `tenant` label to support per-tenant dashboards and alerting isolation.

Common labels:
- `tenant`: tenant id
- `op`: hook operation (`Auth`, `CheckMessage`, `GetGroupMembers`)
- `result`: hook outcome label (stable strings like `ok`, `timeout`, `http_4xx`, `queue_rejected`, ...)

## Alert delivery

Email/SMS/IM-bot delivery is configured in **Alertmanager** (external to MICS). The repo ships only example rules.

