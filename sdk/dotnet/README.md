# .NET SDKs

- `Mics.Client`：客户端连接 SDK（WebSocket + Protobuf），面向业务客户端/工具侧使用。
- `Mics.HookSdk`：服务端 Hook SDK（HTTP/MQ Protobuf 解析 + 验签），面向业务服务端对接使用。

## Samples

- `sdk/dotnet/samples/Mics.HookSample`：最小可运行 Hook 服务端示例（Minimal API + 验签 + Protobuf）
- `sdk/dotnet/samples/Mics.ClientSample`：最小可运行客户端示例（连接 + 发送 + 接收 + ACK）
