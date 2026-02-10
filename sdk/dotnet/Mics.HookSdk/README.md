# Mics.HookSdk (.NET)

服务端 Hook SDK：用于业务方实现 MICS 的同步 HTTP Hook（`/auth`、`/check-message`、`/get-group-members`、`/get-offline-messages`）以及消费 MQ Hook（Kafka）事件时的 Protobuf 解析与验签。

## HTTP Hook 签名（验签）

Gateway 侧签名算法（Base64）：
- HMACSHA256(key = `tenant_secret`)
- 输入拼接：`Serialize(payloadWithMetaSignCleared)` + `RequestId(UTF8)` + `TimestampMs(int64 little-endian)`

SDK 提供：
- `HookSigner.ComputeBase64(...)`
- `HookVerifier.Verify(...)`

## MQ 事件签名（验签）

Gateway 侧签名算法（Base64）：
- HMACSHA256(key = `tenant_secret`)
- 输入：`Serialize(mqEventWithSignCleared)`

SDK 提供：
- `MqEventSigner.ComputeBase64(...)`
- `MqEventVerifier.Verify(...)`
- `MqEventDecoder.TryDecodeConnectAck(...)` / `TryDecodeMessage(...)`
- `MqEventDecoder.TryVerifyAndDecodeConnectAck(...)` / `TryVerifyAndDecodeMessage(...)`

## Protobuf HTTP 读写

- `HookProtobufHttp.ReadAsync(...)`
- `HookProtobufHttp.WriteAsync(...)`

## Minimal API 模板（推荐）

业务方只需要实现 handler，SDK 负责 Protobuf 解析与签名校验：

```csharp
var secrets = new Dictionary<string,string> { ["t1"] = "secret" };
var opts = new MicsHookMapOptions(tid => secrets.TryGetValue(tid, out var s) ? s : null, RequireSign: true);

app.MapMicsAuth(async (req, ct) =>
{
    // TODO: validate token, return config
    return new AuthResponse { Ok = true, UserId = "u1", DeviceId = req.DeviceId };
}, opts);

app.MapMicsCheckMessage(async (req, ct) =>
{
    return new CheckMessageResponse { Allow = true };
}, opts);

app.MapMicsGetGroupMembers(async (req, ct) =>
{
    var resp = new GetGroupMembersResponse();
    resp.UserIds.Add("u1");
    resp.UserIds.Add("u2");
    return resp;
}, opts);
```
