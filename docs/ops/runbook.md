# 运维 Runbook（滚动更新 / 灰度 / 回滚 / 故障排查）

> 需求来源：`docs/MICS（极简IM通讯服务）需求文档V1.0.md`（7.3 / 8.4 部署运维验收）

## 1) 滚动更新（不中断/不丢消息的验收口径）

关键点：
- Gateway 是无状态节点；全局在线路由在 Redis。
- 更新/下线节点时，节点会进入 draining：
  - `/readyz` 返回 `503`
  - 新 WS 握手被拒绝（HTTP 503）
  - 既有 WS 连接将被关闭（CloseCode=4200，`server draining`），客户端应自动重连并迁移到其他节点
- draining 会 best-effort 先从 Redis 注销在线路由，再关闭连接，降低跨节点转发失败概率。

建议 K8s 配置：
- Deployment RollingUpdate：`maxUnavailable: 0`、`maxSurge: 1`
- `terminationGracePeriodSeconds` ≥ `DRAIN_TIMEOUT_SECONDS` + 余量
- PodDisruptionBudget：避免一次性驱逐过多 Pod

操作示例：
```bash
kubectl -n mics rollout status deploy/mics-gateway
kubectl -n mics rollout restart deploy/mics-gateway
kubectl -n mics rollout status deploy/mics-gateway
```

验收观察：
- 客户端：收到 CloseCode=4200 后自动重连成功
- 指标：`mics_shutdown_drain_total` 递增；`mics_grpc_forward_failed_total` 不应持续飙升
- 日志：`shutdown_drain_begin` / `shutdown_drain_done`

## 2) 灰度发布（建议做法）

推荐做法：
- 通过 Deployment 分批次滚动（小 `maxSurge`），配合监控观察。
- 或使用两套 Deployment（例如 `mics-gateway-canary` / `mics-gateway`）并用 Ingress/LB 做少量流量切分（注意 WS 长连接：连接建立时切分即可，连接建立后流量固定在该 Pod）。

最小验收：
- canary 期间 Hook 成功率、gRPC 转发失败率、心跳超时、限流情况无明显劣化。

## 3) 回滚

```bash
kubectl -n mics rollout undo deploy/mics-gateway
kubectl -n mics rollout status deploy/mics-gateway
```

说明：
- 回滚会触发旧版本 Pod 上线，新版本 Pod draining；客户端会因 CloseCode=4200 自动迁移。

## 4) 常见故障排查

### 4.1 连接大量失败/频繁断连

检查：
- `/readyz` 是否频繁 503（是否处于 draining / 频繁重启）
- Redis 连接是否异常（在线路由写入失败会影响多端与跨节点）
- ingress/LB 超时配置是否过短（WS 需较长 read timeout）

指标参考：
- `mics_ws_connected_total` / `mics_ws_disconnected_total`
- `mics_ws_heartbeat_timeouts_total`
- `mics_rate_limited_total{kind="connection_limit"}`

### 4.2 跨节点转发失败

检查：
- `PUBLIC_ENDPOINT` 是否指向具体 Pod（K8s 建议使用 headless service DNS）
- 目标节点是否已下线/网络隔离（dead node cleanup 是否运行）

指标参考：
- `mics_grpc_forward_failed_total`
- `mics_dead_node_cleanups_total`

### 4.3 Hook 超时/失败导致体验劣化

检查：
- 外部 Hook 服务 P95 延迟是否>150ms（默认超时）
- 是否发生租户级隔离/限流（Hook 并发/队列超时）

指标参考：
- `mics_hook_requests_total{result!="ok"}`
- `mics_hook_limiter_rejected_total`

### 4.4 Kafka 不可用

说明：
- MQ Hook 是异步非阻塞；Kafka 不可用会导致事件投递失败/重试/DLQ，但不应阻塞核心转发链路。

指标参考：
- `mics_mq_failed_total` / `mics_mq_retried_total` / `mics_mq_dlq_total`

