using Google.Protobuf;
using Microsoft.AspNetCore.Http;
using Mics.Contracts.Hook.V1;
using Mics.HookSdk;

namespace Mics.Tests;

public sealed class HookProtobufHttpTests
{
    [Fact]
    public async Task ReadAsync_ParsesProtobufBody()
    {
        var msg = new AuthRequest { Meta = new HookMeta { TenantId = "t1", RequestId = "r1", TimestampMs = 1, Sign = "" }, Token = "x", DeviceId = "d1" };
        var bytes = msg.ToByteArray();

        var ctx = new DefaultHttpContext();
        ctx.Request.Body = new MemoryStream(bytes);
        ctx.Request.ContentType = HookProtobufHttp.ProtobufContentType;

        var parsed = await HookProtobufHttp.ReadAsync(AuthRequest.Parser, ctx.Request, CancellationToken.None);
        Assert.Equal(msg, parsed);
    }
}
