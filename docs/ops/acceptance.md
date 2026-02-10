# 验收 Playbook（对齐需求文档第 8 章）

本文档提供可重复执行的验收步骤入口，覆盖功能/接口/部署运维/非功能的“怎么测、测什么、看什么指标/日志”。

> 需求来源：`docs/MICS（极简IM通讯服务）需求文档V1.0.md`

## 0. 前置条件

- Redis 6.2+（必需，用于全局在线路由）
- 可选：Kafka 3.0+（启用 MQ Hook 时）
- Gateway / HookMock 运行并可访问

可选：全仓一键回归（覆盖 SDK / Gateway / 测试）

```powershell
.\scripts\verify.ps1
```

Linux：
```bash
./scripts/verify.sh
```

本地联调参考：`README.md:1`

## 1. 接口/功能验收（8.1 / 8.3）

### 1.1 建连与错误码（6.2.1）

目标：验证 WS URL 参数与 CloseCode 行为符合文档约定。

使用 `.NET ClientWebSocket` 压测工具的 `connect-only` 模式：

```powershell
# 成功建连（应收到 ConnectAck.code=1000）
dotnet run --project tools/Mics.LoadTester -- --url ws://localhost:8080/ws --tenantId t1 --connections 1 --durationSeconds 5 --mode connect-only
```

失败场景：
- Token 无效：CloseCode=4001（或 ConnectAck 非 1000）
- TenantId 无效：CloseCode=4002

说明：
- 由于 WS CloseCode 在不同客户端库的呈现方式可能不同，本仓库工具会在汇总末尾输出 `close_codes:` 统计（Top10）。
- 优雅下线（节点排空）时，服务端可能主动关闭连接（CloseCode=4200，server draining），客户端应触发重连并迁移到其他节点。

### 1.2 心跳保活（4.3.3 / 6.2.2）

```powershell
dotnet run --project tools/Mics.LoadTester -- --url ws://localhost:8080/ws --tenantId t1 --connections 50 --durationSeconds 30 --mode heartbeat --sendQpsPerConn 1
```

观察：
- Gateway 指标：`mics_ws_heartbeat_timeouts_total` 不应持续增长
- 日志：心跳超时会记录 `ws_heartbeat_timeout ... tenant/user/device`

### 1.3 单聊消息（4.4.1）

```powershell
dotnet run --project tools/Mics.LoadTester -- --url ws://localhost:8080/ws --tenantId t1 --connections 200 --durationSeconds 30 --mode single-chat --sendQpsPerConn 2 --payloadBytes 128
```

观察：
- `ack_ok/ack_fail`，以及 `ack_p90/ack_p99`
- 指标：`mics_messages_in_total`、`mics_deliveries_total`、`mics_messages_failed_total`

### 1.4 群聊消息（4.4.2）

前置：HookMock 需配置 `groupId -> members` 映射（示例环境变量见 `README.md:1`）。

```powershell
dotnet run --project tools/Mics.LoadTester -- --url ws://localhost:8080/ws --tenantId t1 --connections 200 --durationSeconds 30 --mode group-chat --groupId group-1 --sendQpsPerConn 1 --payloadBytes 128
```

观察：
- 指标：`mics_group_messages_total`、`mics_group_members_total`、`mics_group_fanout_nodes_total`
- 离线缓冲行为：`mics_group_offline_buffered_total` / `mics_group_offline_buffer_skipped_total` / `mics_offline_buffer_skipped_total`

### 1.5 离线消息（方案三：best-effort）

目标：验证「离线事件通知（MQ）」+「上线拉取补发（Hook）」的 best-effort 链路不阻塞核心转发，且租户隔离。

前置：租户在 `/auth` 返回的 `TenantRuntimeConfig.offline_use_hook_pull=true`（HookMock 可通过环境变量 `OFFLINE_USE_HOOK_PULL=true` 便捷开启）。

观察：
- MQ 侧：`mics_mq_published_total{event_type="OfflineMessage"}` / `mics_mq_dropped_total{reason="tenant_quota"|"queue_full"}`
- Gateway 侧：`mics_offline_notified_total`（离线事件入队成功）
- Hook 拉取：`mics_hook_requests_total{op="GetOfflineMessages",result="ok"|"timeout"|...}`、`mics_hook_get_offline_messages_total{result="ok"|"fail"|"degraded"}`
- 补发计数：`mics_offline_drained_from_hook_total`（Hook补发）与 `mics_offline_drained_total`（本地短期缓冲回退补发）

## 2. Hook 降级/熔断验收（8.1 / 5.2）

目标：Hook 超时/失败不会阻塞核心转发链路，并且有可观测性。

建议步骤：
1. 将 HookMock 人为延迟（或返回 5xx），触发 `150ms` 超时
2. 重跑单聊/群聊压测（上节命令）

观察：
- 指标：`mics_hook_requests_total{result="timeout"|"http_5xx"|...}` 上升
- 日志：`hook_request_failed tenant/op/result/url/requestId`（已限频）
- 若启用降级策略：`mics_hook_check_message_total{result="degraded"}` 上升但消息仍可投递（取决于租户策略）

## 3. 集群验收（8.1 / 8.4）

目标：验证跨节点转发与节点故障清理。

建议步骤（示例）：
1. 启动 2 个 Gateway（不同 `NODE_ID`，不同监听端口），共享同一 Redis
2. 让用户 A 连到 node-1，用户 B 连到 node-2
3. A 向 B 发单聊消息，应发生跨节点 gRPC 转发
4. 停止 node-2，观察 dead-node cleanup 与路由清理

观察：
- 指标：`mics_grpc_forward_failed_total` 在正常链路应接近 0
- 指标：`mics_dead_node_cleanups_total` 增加后，路由应被清理
- 日志：`dead_node_cleanup_start/done`

## 4. 部署运维验收入口（8.4）

K8s 示例清单：
- `deploy/k8s/README.md`
- Ingress：`deploy/k8s/gateway.ingress.yaml`
- HPA：`deploy/k8s/gateway.hpa.yaml`
- HPA 说明：`docs/ops/hpa.md`
- Prometheus Operator：`deploy/k8s/monitoring/gateway-servicemonitor.yaml`、`deploy/k8s/monitoring/gateway-prometheusrule.yaml`
- 运维 Runbook：`docs/ops/runbook.md`

说明：
- CPU HPA 可直接使用；“按连接数”扩缩容需要 Prometheus Adapter 将 `mics_ws_connections` 暴露为 Pods 自定义指标。
