# MICS V1 Roadmap（基于需求文档 V1.0）

本文档用于把 `docs/MICS（极简IM通讯服务）需求文档V1.0.md` 的剩余需求拆分为可交付里程碑，便于持续实现与验收。

## 已实现（截至当前仓库状态）

- WebSocket 接入：`/ws?tenantId&token&deviceId`，Protobuf binary 帧
- 同步 HTTP Hook：`/auth`、`/check-message`、`/get-group-members`（150ms 超时、降级策略）
- 多租户强隔离：链路 TenantId 绑定，Redis Key 按 `{TenantId}:...`
- Redis 在线路由：`{TenantId}:online:{UserId}`（device->node/endpoint/conn）
- 节点间 gRPC 转发：单聊/批量、离线缓冲投递与拉取
- 离线短期缓冲：仅内存、TTL
- 限流（部分）：租户消息 QPS、用户多端上限；租户连接数上限（best-effort Redis 计数 + TTL lease）
- 结构化日志（JSON）与 `/metrics`（基础 counters/gauges）
- Hook 熔断（按租户/操作）：连续失败开路、半开单飞、成功闭合
- MsgId 去重：Redis SETNX + TTL（失败降级放行）

## 里程碑拆分（推荐顺序）

### M1：网关核心完备（验收以功能为主）
1. **错误码与协议细化**
   - 明确 WebSocket close code / ServerError code 枚举（文档 6.2.1 示例）
   - Ack 语义：SENT/FAILED 的判定、reason 字段对齐
2. **心跳与连接回收**
   - 租户级心跳超时（默认 30s）：无数据/心跳时断开并清理 Redis 在线状态
3. **租户连接数上限（强一致实现）**
   - 解决节点崩溃导致计数漂移：引入“按连接 lease + TTL 续租”或“按 node lease + 在线路由聚合”方案
4. **群聊扇出优化**
   - 成员列表分段拉取/分页（若 Hook 支持）
   - 节点分桶批发避免重复与过多 Redis 查询

### M2：Hook 体系完善（验收以稳定性/隔离为主）
1. **Hook 请求签名完善**
   - /auth 签名：引入租户预置 secret（配置/密钥中心）或 registry hook 下发
   - 请求字段：TenantId/RequestId/Timestamp/Sign 全链路固定
2. **熔断/降级策略产品化**
   - per-tenant 配置：阈值、开路时长、半开策略
   - 指标：成功率/超时率/熔断次数/降级次数（租户维度）
3. **Hook 并发与隔离**
   - 每租户并发上限与队列（避免某租户拖垮线程池）
   - 超时统一与可观测（TraceId 贯通）

### M3：MQ Hook（Kafka）（验收以事件可靠性为主）
1. **事件模型对齐（文档 6.3.2）**
   - `MqEvent`：TenantId/EventType/MsgId/UserId/DeviceId/NodeId 等字段完整化
2. **Kafka 生产者实现（AOT 兼容）**
   - 选型：必须验证 NativeAOT 支持与无反射依赖
3. **内存重试队列 + DLQ**
   - 指数退避、最大次数（默认 3）
   - DLQ Topic：`im-mics-{TenantId}-event-dlq`

### M4：强制技术栈补齐（验收以 AOT/性能为主）
1. **源生成 DI**
   - 迁移到源生成容器（例如 Jab）或 .NET 官方源生成 DI（若可用），避免运行时反射 DI
2. **Pipelines + ValueTask（核心链路）**
   - WebSocket 收包/解包与写包优化（减少分配、减少 ToArray）
3. **性能/压测脚本**
   - 单机 10w 连接、1w QPS 小包基准；输出延迟与资源曲线

### M5：多语言 SDK（验收以生态为主）
1. Hook SDK（优先：Java/Go/Node/.NET）  
2. 客户端 SDK（优先：TS/Flutter/小程序/Android/iOS）  
3. 示例与一键联调

## Status Update (2026-02-08)
- Implemented M3: Kafka MQ Hook (MqEvent publish + in-memory retry + DLQ).
- Implemented M4-1: source-generated DI (Jab) for MICS services.
- Implemented M4-2: Pipelines + pooled Protobuf IO for WebSocket/gRPC hot path.
- Implemented M4-3: load/perf tool using .NET ClientWebSocket (`tools/Mics.LoadTester`).
- Implemented M1-3: crash-safe tenant/user connection limits via Redis lease zsets (auto-renew + dead-node cleanup).
- Implemented M1-1: message protocol validation + ACK semantics (FAILED reasons for missing fields etc).
- Implemented M1-4: group fanout optimization (chunked route fetch + offline buffer cap + extra metrics).

## Status Update (2026-02-09)
- Implemented M2-1: HTTP Hook signing hardening (fail-closed when sign required but secret missing) and MQ event signing (added `MqEvent.sign` + gateway-side HMAC generation).
- Added: initial .NET client connection SDK (`sdk/dotnet/Mics.Client`) with connect/heartbeat/auto-reconnect/send+ack.
- Added: initial .NET hook SDK (`sdk/dotnet/Mics.HookSdk`) with Protobuf HTTP helpers + HTTP/MQ signature verify utilities.
- Added: .NET hook SDK Minimal API endpoint mappings + MQ event decoder and runnable dotnet samples (`sdk/dotnet/samples/*`).
- Added: initial TypeScript (Web/H5) client SDK under `sdk/ts` with connect/send+ack/heartbeat/reconnect and a runnable web demo (`sdk/ts/samples/web`).

## 验收建议（与文档第 8 章对齐）

- 功能：连接、单聊/群聊、离线补发、隔离、Hook 降级、限流
- 非功能：延迟/QPS/连接数、Hook 成功率、崩溃恢复、指标与日志可追溯
- 部署：Docker/K8s、一键部署、HPA、健康检查
