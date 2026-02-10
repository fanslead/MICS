# MICS V1 需求覆盖度 Review（对齐需求文档 V1.0）

> Source of Truth：`docs/MICS（极简IM通讯服务）需求文档V1.0.md`

本文件用于快速核对「需求文档」与仓库当前实现的覆盖度，明确仍需补齐的缺口与优先级建议。

## 结论概览

- **核心链路（通讯管道）已覆盖**：WebSocket 接入、Protobuf 二进制帧、HTTP Hook 外置、Redis 全局在线路由、gRPC 跨节点转发、短期离线缓冲（内存）、限流、熔断与降级、结构化日志与 Prometheus `/metrics`。
- **交付/运维已覆盖**：Dockerfile、K8s manifests（含 HPA/PDB/Ingress 示例）、监控与告警示例、Runbook、验收 Playbook、压测工具。
- **SDK 生态已覆盖到 P1 目标**：服务端 Hook SDK（Java/Go/Node ESM/.NET），客户端 SDK（TS Web/H5、.NET、Flutter、微信小程序、Android Kotlin）。
- **主要缺口**：iOS（Swift）客户端 SDK（用户已明确暂不做），以及“性能指标达标”仍需在目标环境中按验收 Playbook 实测并留存数据（属于验收工作而非代码缺口）。

## 覆盖映射（按需求文档章节）

### 4.x 功能需求

- **4.1 SaaS 多租户**：TenantId 全链路传递与校验；租户级隔离（Redis key/topic/限流/Hook 配置维度）。
  - 位置：`src/Mics.Gateway/*`（连接、路由、Hook、限流、指标均按 tenant 维度埋点/隔离）
- **4.2 集群化**：无状态节点；Redis 在线路由；节点间 gRPC 通信；dead-node cleanup。
  - 位置：`src/Mics.Gateway/Cluster`、`src/Mics.Gateway/Grpc`、`src/Mics.Gateway/Infrastructure/Redis`
- **4.3 连接与生命周期**：WS 连接管理、应用层心跳、回收/排空。
  - 位置：`src/Mics.Gateway/Ws`、`src/Mics.Gateway/Connections`
- **4.4 消息路由**：单聊/群聊路由与投递；消息去重（MsgId）；消息安全（Hook/MQ 侧签名；客户端可选加密）。
  - 位置：`src/Mics.Gateway/Protocol`、`src/Mics.Gateway/Group`、`src/Mics.Gateway/Offline`、`src/Mics.Gateway/Hook`、`src/Mics.Gateway/Mq`
- **4.5 Hook**：同步 HTTP Hook（三个必选接口）+ 异步 MQ Hook（Kafka，按租户 topic）；超时 150ms；熔断/降级。
  - 位置：`src/Mics.Gateway/Hook`、`src/Mics.Gateway/Mq`
- **4.6 多语言 SDK**：
  - Hook SDK：`sdk/java/`、`sdk/go/`、`sdk/node/mics-hook-sdk/`（ESM-only）、`sdk/dotnet/Mics.HookSdk/`
  - Client SDK：`sdk/ts/`、`sdk/dotnet/Mics.Client/`、`sdk/flutter/mics_client_sdk/`、`sdk/wechat/mics-client-sdk/`、`sdk/android/mics-client-sdk/`
- **4.7 监控/日志/告警**：JSON 结构化日志、Prometheus 指标、告警示例。
  - 位置：`src/Mics.Gateway/Metrics`、`docs/ops/monitoring.md`、`deploy/k8s/monitoring/*`

### 5.x 非功能需求

- **性能指标**：仓库提供压测入口与指标输出，但“是否达标”需在目标部署环境按验收流程实测。
  - 工具：`tools/Mics.LoadTester/`
  - 入口：`docs/ops/acceptance.md`
- **AOT 目标**：部署侧通过 Dockerfile 以 `PublishAot=true` 发布（Linux-x64）。
  - 位置：`deploy/docker/Dockerfile.gateway`

### 6.x 接口规范

- WebSocket `/ws?tenantId=&token=&deviceId=`：服务端实现与 SDK/压测工具对齐。
- Hook：`Content-Type: application/protobuf`，Protobuf payload；签名字段由服务端发起并由 Hook SDK 校验。
- MQ：按租户 topic（`im-mics-{TenantId}-event`），Protobuf 事件结构。

## 仍需补齐（按优先级）

### P0（若要对外交付必须明确）

1. **iOS（Swift）客户端 SDK**：当前未实现（用户已明确暂不做）。
2. **目标环境性能验收留档**：按 `docs/ops/acceptance.md` 跑压测/故障演练并记录指标与日志（属于验收工作流）。

### P1（工程化增强，建议做但不阻塞核心功能）

1. **CI 自动化**：在 Linux runner 上跑 `dotnet test` + Node/Java（必要时 Go）回归；可选做 Docker AOT 构建 smoke test。
2. **SDK 发布与版本策略**：npm/MavenCentral/NuGet/pub.dev/Gradle 等发布流水线与语义化版本规范（需求文档未强制，但工程交付常用）。

## 一键回归

- Windows：`.\scripts\verify.ps1`
- Linux：`./scripts/verify.sh`

