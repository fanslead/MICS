# Mics.Client (.NET)

轻量的 MICS 客户端连接 SDK：封装 WebSocket 连接、心跳、自动重连、消息发送/ACK 等。

## Quickstart

```csharp
using Mics.Client;

await using var client = new MicsClient();
client.Connected += s => Console.WriteLine($"connected user={s.UserId} node={s.NodeId}");
client.DeliveryReceived += d => Console.WriteLine($"delivery msgId={d.Message?.MsgId}");
client.AckReceived += a => Console.WriteLine($"ack msgId={a.MsgId} status={a.Status}");

await client.ConnectAsync(
    new Uri("ws://localhost:8080/ws"),
    tenantId: "t1",
    token: "valid:u1",
    deviceId: "dev1");

var ack = await client.SendSingleChatAsync("u2", new byte[] { 1, 2, 3 });
Console.WriteLine($"send ack={ack.Status} reason={ack.Reason}");
```

## Options

通过 `MicsClientOptions` 可配置：
- `HeartbeatInterval`：发送 `ClientFrame.heartbeat_ping` 的周期
- `AutoReconnect`：断线后自动重连
- `AckTimeout` / `MaxSendAttempts`：ACK 等待与重试
- `MessageCrypto`：可选的消息体加密（AES-GCM），仅对 `MessageRequest.msg_body` 生效

说明：
- 当 `AutoReconnect=true` 且连接处于重连中时，`SendSingleChatAsync` / `SendGroupChatAsync` 会等待重连恢复后再继续发送（直到超时/取消）。
