# MICS Gateway MVP 设计（基于需求文档 V1.0）

本文档将 `docs/MICS（极简IM通讯服务）需求文档V1.0.md` 的核心链路（连接/路由/转发/Hook 外置/多租户隔离/无状态集群）落成可实现的 MVP 规格，并作为本仓库后续实现的依据。

## 目标与范围

**MVP 交付：**

- `.NET 10 AOT` 网关服务：`ASP.NET Core WebSocket`（客户端接入）+ `gRPC/HTTP2`（节点间转发）
- 协议：Protobuf（编译期生成），WebSocket 仅使用二进制帧
- 多租户强隔离：全链路 `TenantId`，Redis Key 按租户命名
- 全局在线路由：Redis 存储在线状态（仅路由，不存业务消息）
- Hook 外置：同步 HTTP Hook（`/auth`、`/check-message`、`/get-group-members`），超时 150ms，支持降级
- 离线短期缓冲：仅内存，TTL 默认 5 分钟，重启清空
- 可观测：结构化 JSON 日志（含 `TenantId/NodeId/TraceId/MsgId`），Prometheus `/metrics`

**MVP 不做：**

- 任何业务存储（消息漫游、已读、用户/群资料等）
- Kafka MQ Hook 的真实接入（仅预留接口/开关，默认禁用）

## 关键决策（MVP 固定）

- 租户 `/auth` Hook URL 发现：静态配置映射 `TenantId -> AuthHookBaseUrl`
- `/check-message`：阻断式校验；150ms 内 deny 则拒绝；超时/失败 **降级放行** 并记录日志/指标
- 签名：`HMAC-SHA256`（`tenantSecret` 用于网关->Hook 的签名）

## 对外接口（精简版）

### WebSocket（文档 6.2）

`/ws?tenantId={TenantId}&token={Token}&deviceId={DeviceId}`

- 连接成功：网关发 `ConnectAck(code=1000)`
- 鉴权失败：关闭连接，错误码 `4001`
- 租户无效：关闭连接，错误码 `4002`

WebSocket 数据帧：binary(Protobuf)。

### 同步 HTTP Hook（文档 6.3.1）

HTTP POST，`Content-Type: application/protobuf`，超时 150ms：

- `{AuthHookBaseUrl}/auth`：`AuthRequest -> AuthResponse`
- `{TenantHookBaseUrl}/check-message`：`CheckMessageRequest -> CheckMessageResponse`
- `{TenantHookBaseUrl}/get-group-members`：`GetGroupMembersRequest -> GetGroupMembersResponse`

签名字段：`TenantId/RequestId/Timestamp/Sign`。`Sign = base64(HMACSHA256(tenantSecret, payloadBytes || requestId || timestampLE))`。

### 节点间 gRPC

- 单聊跨节点转发：`ForwardSingle`
- 群聊按节点分桶转发：`ForwardBatch`
- 离线缓冲投递与拉取：`BufferOffline` / `DrainOffline`

## Redis Schema（强隔离）

- 在线路由：`{TenantId}:online:{UserId}`（Hash）
  - field = `DeviceId`
  - value = `NodeId|Endpoint|ConnectionId|OnlineAtUnixMs`
- 节点注册：`nodes:{NodeId}`（Hash，带 TTL/续租）

## 离线缓冲（仅内存）

- per-tenant/per-user 队列，TTL 默认 5 分钟
- 目标用户上线时触发 drain（本地或跨节点 gRPC 拉取），投递后清空

## 可观测性

- 日志：JSON，至少包含 `TenantId/NodeId/TraceId/MsgId/Time/Level/Content`
- 指标：连接数/消息计数/Hook 成功失败/限流次数等，暴露 `/metrics`

