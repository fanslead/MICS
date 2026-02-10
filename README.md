# MICS（极简 IM 通讯服务）— Minimal IM Communication Service

> 需求文档驱动：`docs/MICS（极简IM通讯服务）需求文档V1.0.md` 为唯一 Source of Truth。

MICS 是一个 **SaaS 多租户强隔离、无状态集群、零存储** 的 IM 通讯管道服务：只负责 **连接 / 路由 / 转发**，不承载业务存储与业务逻辑；鉴权/权限/审计/存储等全部通过 **HTTP/MQ Hook 外置**。

## 关键约束（必须遵守）

- **纯通讯管道**：只做连接、路由、转发。
- **零存储**：不存储任何业务数据（消息、用户、群、关系链等）；仅允许内存级临时状态（连接/在线路由/短期离线缓冲）。
- **Hook 外置**：业务逻辑由外部系统实现（HTTP Hook + Kafka MQ Hook）。
- **多租户强隔离**：全链路携带 `TenantId`；Redis Key / MQ Topic / 限流 / Hook 配置均按租户隔离。
- **无状态集群**：节点不持久化本地状态；全局在线路由依赖 Redis；支持水平扩缩容。
- **生产运行环境：Linux**（K8s/Docker）。Windows 仅用于本地开发联调。

## 架构概览

- Client ⇄ Gateway：`ASP.NET Core WebSocket`（二进制帧，Protobuf）
- Gateway ⇄ Gateway：`gRPC/HTTP2`（跨节点转发/离线缓冲 home-node 读写）
- 全局在线路由：`Redis`（只存在线与路由信息，不存业务消息）
- Hook：
  - 同步 HTTP Hook：`/auth`、`/check-message`、`/get-group-members`（`application/protobuf`，超时 150ms）
  - 异步 MQ Hook：Kafka Topic：`im-mics-{TenantId}-event`（Protobuf）

## 接口一览

- `WS /ws?tenantId={TenantId}&token={Token}&deviceId={DeviceId}`
- `GET /healthz`：liveness
- `GET /readyz`：readiness（draining 时返回 503）
- `GET /metrics`：Prometheus 文本

错误码示例：
- WebSocket CloseCode：`4001` 鉴权失败、`4002` 租户无效、`4100` 心跳超时、`4200` draining、`4429` 限流
- `ServerFrame.error.code=4400`：Protobuf 解码失败

## 本地快速启动（开发联调）

1) 启动 Redis（示例）

```powershell
docker run --rm -p 6379:6379 redis:6.2
```

2) 启动 HookMock（默认 `http://localhost:8081`）

```powershell
dotnet run --project src/Mics.HookMock --urls http://localhost:8081
```

3) 启动 Gateway（默认 `http://localhost:8080`）

```powershell
$env:REDIS__CONNECTION="localhost:6379"
$env:TENANT_AUTH_MAP='{"t1":"http://localhost:8081"}'
$env:TENANT_HOOK_SECRETS='{"t1":"dev-secret-t1"}'  # 用于签名 /auth（可选；若启用 HOOK_SIGN_REQUIRED 则必填）
$env:HOOK_SIGN_REQUIRED="false"
$env:WS_KEEPALIVE_INTERVAL_SECONDS="30"           # WebSocket Ping keepalive (0=disable)

# Safety knobs (0=disable)
$env:MAX_MESSAGE_BYTES="1048576"                  # msg_body 最大 bytes
$env:OFFLINE_BUFFER_MAX_MESSAGES_PER_USER="128"   # 单用户离线缓冲条数上限
$env:OFFLINE_BUFFER_MAX_BYTES_PER_USER="1048576"  # 单用户离线缓冲总 bytes 上限

# Group fanout tuning
$env:GROUP_ROUTE_CHUNK_SIZE="256"
$env:GROUP_MEMBERS_MAX_USERS="200000"
$env:GROUP_OFFLINE_BUFFER_MAX_USERS="1024"        # 群聊离线缓冲最多用户数（0=禁用群聊离线缓冲）

# Hook isolation defaults
$env:HOOK_MAX_CONCURRENCY="32"
$env:HOOK_QUEUE_TIMEOUT_MS="10"
$env:TENANT_HOOK_MAX_CONCURRENCY='{"t1":16}'      # 可选：租户覆写

# Node identity
$env:NODE_ID="node-1"
$env:PUBLIC_ENDPOINT="http://localhost:8080"

# Optional: MQ Hook (Kafka)
$env:KAFKA__BOOTSTRAP_SERVERS="localhost:9092"
$env:KAFKA_MAX_ATTEMPTS="3"
$env:KAFKA_QUEUE_CAPACITY="50000"
$env:KAFKA_RETRY_BACKOFF_MS="50"
$env:KAFKA_IDLE_DELAY_MS="5"

dotnet run --project src/Mics.Gateway --urls http://localhost:8080
```

4) WebSocket 连接示例

`ws://localhost:8080/ws?tenantId=t1&token=valid:u1&deviceId=dev1`

## 配置（环境变量）

主要环境变量示例见 `deploy/k8s/gateway.configmap.yaml`。

核心配置（节选）：
- `REDIS__CONNECTION`：必填
- `TENANT_AUTH_MAP`：必填（示例：`{"t1":"http://hookmock:8081"}`）
- `HOOK_SIGN_REQUIRED` + `TENANT_HOOK_SECRETS`：可选（签名/防篡改）
- `MAX_MESSAGE_BYTES`：入站 `msg_body` 最大 bytes（0=禁用）
- `OFFLINE_BUFFER_MAX_MESSAGES_PER_USER` / `OFFLINE_BUFFER_MAX_BYTES_PER_USER`：单用户离线缓冲上限（0=禁用）
- `KAFKA__BOOTSTRAP_SERVERS`：不为空则启用 MQ Hook（Kafka）

## MQ 事件语义（重要）

当消息通过 `/check-message` 校验后，Gateway 会先尝试投递（在线下发或写入短期离线缓冲），**投递尝试完成后**再异步投递 MQ 事件到租户 Topic（`im-mics-{TenantId}-event`）。

## SDK

**客户端 SDK**
- TypeScript（Web/H5）：`sdk/ts/`（sample：`sdk/ts/samples/web`）
- .NET：`sdk/dotnet/Mics.Client/`
- Flutter：`sdk/flutter/mics_client_sdk/`（sample：`sdk/flutter/mics_client_sdk/example`）
- 微信小程序：`sdk/wechat/mics-client-sdk/`（sample：`sdk/wechat/mics-client-sdk/samples/miniprogram`）
- Android（Kotlin）：`sdk/android/mics-client-sdk/`（sample：`sdk/android/mics-client-sdk/samples/console`）
- iOS（Swift）：暂未实现（当前无 iOS 环境）

**服务端 Hook SDK**
- Node.js（ESM-only）：`sdk/node/mics-hook-sdk/`
- Java：`sdk/java/`
- Go：`sdk/go/`
- .NET：`sdk/dotnet/Mics.HookSdk/`

## 压测工具（.NET ClientWebSocket）

项目内置压测/基准工具：`tools/Mics.LoadTester/`（仅依赖 .NET）。

```powershell
# 心跳压测（仅测 WS 基础链路）
dotnet run --project tools/Mics.LoadTester -- `
  --url ws://localhost:8080/ws --tenantId t1 `
  --connections 1000 --rampSeconds 10 --durationSeconds 30 `
  --mode heartbeat --sendQpsPerConn 1

# 单聊压测（走 /check-message + Redis 路由 + 本地/跨节点转发 + 离线缓冲）
dotnet run --project tools/Mics.LoadTester -- `
  --url ws://localhost:8080/ws --tenantId t1 `
  --connections 200 --rampSeconds 10 --durationSeconds 30 `
  --mode single-chat --sendQpsPerConn 2 --payloadBytes 128
```

群聊压测需要 HookMock 提供成员列表（示例：`HOOK_GROUP_MEMBERS='{\"group-1\":[\"u1\",\"u2\",\"u3\"]}'`），然后使用 `--mode group-chat --groupId group-1`。

## 部署（示例）

- Docker：`deploy/docker/Dockerfile.gateway`、`deploy/docker/Dockerfile.hookmock`
- Kubernetes：`deploy/k8s/README.md`（含 ConfigMap/HPA/PDB/Ingress/Monitoring 示例）

## 可观测性与验收

- 运维 Runbook：`docs/ops/runbook.md`
- 监控：`docs/ops/monitoring.md`
- HPA：`docs/ops/hpa.md`
- 验收 Playbook：`docs/ops/acceptance.md`

## 一键回归（全仓）

```powershell
.\scripts\verify.ps1
```

```bash
./scripts/verify.sh
```

