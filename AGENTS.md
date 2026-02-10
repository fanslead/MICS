# MICS（极简 IM 通讯服务）— Agent 指南

本仓库当前以需求文档驱动（`docs/`）。任何实现与改动以需求为准，尤其是「强制技术栈」与「零存储/强隔离/无状态集群」约束。

每次执行任务前，请确认需求与需求文档一致，然后利用你拥有的技能协助你完成任务。

## 需求来源（Source of Truth）

- `docs/MICS（极简IM通讯服务）需求文档V1.0.md`
- 若实现与文档冲突：优先修正文档或按文档调整实现；不要“将就实现”。

## 项目定位（必须遵守）

- **纯通讯管道**：只负责连接、路由、转发。
- **零存储**：不存储任何业务数据（消息、用户、群、关系链等）；仅允许内存级临时状态（连接/在线路由/短期离线缓冲）。
- **Hook 外置**：鉴权/权限/审计/存储等业务逻辑全部通过 HTTP/MQ Hook 下沉到外部系统。
- **SaaS 多租户强隔离**：全链路携带 `TenantId`；资源、连接、消息、限流、Hook 配置均租户隔离。
- **无状态集群**：节点不持久化本地状态；全局在线路由依赖 Redis；支持水平扩缩容。

## 强制技术栈（摘自文档 3.2）

- 运行时：`.NET 10 AOT`（原生编译，无 JIT、无反射、无动态代码，支持单文件发布）
- 传输：`ASP.NET Core WebSocket`（客户端接入）+ `gRPC/HTTP2`（节点间通信）
- 序列化：`Protobuf（源生成）`（禁止 `Newtonsoft.Json` / 反射序列化）
- 依赖注入：`.NET 源生成 DI`（禁止运行时反射 DI）
- 全局路由：`Redis`（仅存在线状态/路由信息，不存业务消息）
- 网络 IO：`System.IO.Pipelines` + `ValueTask`（低分配/低 GC）
- Hook：同步 `HTTP Hook` + 异步 `MQ Hook（Kafka）`（租户隔离，支持超时/熔断/降级，不阻塞核心转发）
- 可观测性：结构化日志（JSON）+ `Prometheus`（日志需携带 `TenantId/NodeId/TraceId`）
- 部署：`Docker` + `Kubernetes`

## 协议与接口要点

- WebSocket：`/ws?tenantId={TenantId}&token={Token}&deviceId={DeviceId}`（见文档 6.2）
- 节点间转发：gRPC（低延迟高吞吐；要求 AOT 兼容）
- 所有业务接口数据载荷：**Protobuf**（见文档 6 章的消息/Hook 结构）
- 错误码（示例）：连接成功 `1000`；鉴权失败 `4001`；租户无效 `4002`（详见文档 6.2.1）

## Hook 约束（核心）

- 同步 HTTP Hook（HTTP POST，`Content-Type: application/protobuf`，超时 **150ms**，见文档 6.3.1）：
  - `/auth`：租户鉴权（返回 `UserId/DeviceId/租户配置` 等）
  - `/check-message`：消息合法性校验（允许/拒绝/原因）
  - `/get-group-members`：群成员获取（`List<UserId>`）
- 异步 MQ Hook（Kafka，租户 Topic：`im-mics-{TenantId}-event`，见文档 6.3.2）：
  - 事件包含 `TenantId/EventType/MsgId/UserId/DeviceId/.../NodeId` 等字段
- **降级原则**：Hook 超时/失败时必须可熔断与降级，避免影响核心通讯链路（见文档 4.5、5.2）。

## 多租户隔离规则（实现时不要破坏）

- 所有链路必须显式携带并校验 `TenantId`。
- Redis Key / MQ Topic / 限流规则 / Hook 配置：按租户隔离，命名遵循 `{TenantId}:{资源标识}`（见文档 4.1.2）。
- 单租户 Hook 异常、限流触发、连接过载：不得影响其他租户。

## 可观测性要求

- 日志：统一 JSON 结构化，至少包含 `TenantId/NodeId/TraceId/MsgId/Time/Level/Content`（见文档 4.7.2）。
- 关键事件必须记录：连接建连/断连、消息转发失败、Hook 失败、限流触发、节点故障等。
- 指标：集群/连接/消息/Hook/资源指标；支持租户级监控与告警（见文档 4.7）。

## 非功能目标（实现与优化的硬指标）

- 连接：单节点 ≥10w 长连接；集群 ≥100w；连接成功率 ≥99.99%
- 吞吐：单节点消息 QPS ≥1w（<1KB）；集群 ≥10w；万人群转发延迟 ≤10ms
- 延迟：单节点转发 ≤1ms；跨节点 ≤5ms（内网）；端到端 ≤50ms（公网）
- GC：核心链路零 GC；非核心链路 GC ≤1 次/分钟；停顿 ≤1ms
- 资源：8C16G 满负载 CPU ≤70%、内存 ≤8G（10w 连接）；单连接内存 ≤800KB
- AOT：启动 ≤1s；单文件 ≤50MB；无运行时依赖（见文档 5.1）

## 部署约束

- 运行环境：Linux（CentOS 8+/Ubuntu 20.04+），Windows
- 依赖版本：Redis 6.2+（高可用）、Kafka 3.0+（至少 3 节点）、Nginx/Envoy 支持 WebSocket 长连接
- 目标：支持 K8s 滚动更新、灰度发布、HPA 自动扩缩容、故障自愈（见文档 7 章）

## 提交/改动守则（面向实现阶段）

- 任何新增功能先确认是否应通过 Hook 外置；避免把业务逻辑“塞进网关”。
- 任何协议/字段变更必须保持 Protobuf 向后兼容（仅新增字段，不破坏旧字段语义）。
- 严禁引入反射/动态代码/运行时生成（AOT 不兼容风险）。
- 若新增工程结构、proto、部署脚本等，请同时补齐文档（至少在 `docs/` 记录设计与接口变化）。

