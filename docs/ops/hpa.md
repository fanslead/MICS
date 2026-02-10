# HPA（按 CPU / 按连接数）

> 需求来源：`docs/MICS（极简IM通讯服务）需求文档V1.0.md`（7.3 / 8.4 的扩缩容验收）

## 1) CPU HPA（推荐默认）

仓库内置示例：`deploy/k8s/gateway.hpa.yaml`

优点：
- K8s 原生资源指标即可工作（metrics-server）
- 交付/验收成本最低

## 2) 按连接数 HPA（需要额外组件）

目标：根据每个 Pod 的 `mics_ws_connections{node="..."}` 做自动扩缩容。

现状：
- Gateway 已暴露 `mics_ws_connections` 指标（Gauge）
- HPA 示例中已预留 Pods 自定义指标写法（需要 Prometheus Adapter）

### 2.1 前置组件

- Prometheus（建议 Prometheus Operator；示例见 `deploy/k8s/monitoring/gateway-servicemonitor.yaml`）
- Prometheus Adapter（将 Prometheus 指标映射为 K8s Custom Metrics API）

### 2.2 指标映射思路（概念）

1. Prometheus 抓取 `/metrics`
2. Adapter 规则把 `mics_ws_connections` 映射为 `pods` 维度的 custom metric（按 `node` label 或 pod label 进行关联）
3. `deploy/k8s/gateway.hpa.yaml` 启用 `type: Pods` 的 metric 配置

注意：
- 需要保证 “某 pod 的连接数指标” 能被映射为该 pod 的 custom metric；不同 Adapter/安装方式映射规则不同。
- 本仓库提供的是 **HPA 形态与接入点**，具体 Adapter 安装与规则应由平台侧统一维护（与租户隔离、告警平台一致）。

