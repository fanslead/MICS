using System.Net;
using System.Net.Http.Headers;
using Google.Protobuf;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Mics.Contracts.Hook.V1;
using Mics.HookSdk;

namespace Mics.Tests;

public sealed class HookSdkMappingTests
{
    [Fact]
    public async Task MapMicsAuth_ValidSign_InvokesHandler_AndReturnsProtobuf()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        var app = builder.Build();

        var secrets = new Dictionary<string, string>(StringComparer.Ordinal) { ["t1"] = "secret" };
        app.MapMicsAuth(
            handler: (req, ct) =>
            {
                Assert.Equal("t1", req.Meta.TenantId);
                Assert.Equal("valid:u1", req.Token);
                return new ValueTask<AuthResponse>(new AuthResponse
                {
                    Ok = true,
                    UserId = "u1",
                    DeviceId = req.DeviceId,
                    Reason = "",
                });
            },
            options: new MicsHookMapOptions(tenantId => secrets.TryGetValue(tenantId, out var s) ? s : null, RequireSign: true));

        await app.StartAsync();
        var client = app.GetTestClient();

        var req = new AuthRequest
        {
            Meta = new HookMeta { TenantId = "t1", RequestId = "r1", TimestampMs = 123, Sign = "" },
            Token = "valid:u1",
            DeviceId = "d1",
        };
        var payload = req.Clone();
        payload.Meta.Sign = "";
        req.Meta.Sign = HookSigner.ComputeBase64("secret", req.Meta, payload);

        using var content = new ByteArrayContent(req.ToByteArray());
        content.Headers.ContentType = new MediaTypeHeaderValue(HookProtobufHttp.ProtobufContentType);

        var resp = await client.PostAsync("/auth", content);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var bytes = await resp.Content.ReadAsByteArrayAsync();
        var parsed = AuthResponse.Parser.ParseFrom(bytes);
        Assert.True(parsed.Ok);
        Assert.Equal("u1", parsed.UserId);
        Assert.NotNull(parsed.Meta);
        Assert.Equal("t1", parsed.Meta.TenantId);
    }

    [Fact]
    public async Task MapMicsCheckMessage_MissingSign_WhenRequired_ReturnsAllowFalse()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        var app = builder.Build();

        app.MapMicsCheckMessage(
            handler: (_, _) => new ValueTask<CheckMessageResponse>(new CheckMessageResponse { Allow = true }),
            options: new MicsHookMapOptions(_ => "secret", RequireSign: true));

        await app.StartAsync();
        var client = app.GetTestClient();

        var req = new CheckMessageRequest
        {
            Meta = new HookMeta { TenantId = "t1", RequestId = "r1", TimestampMs = 1, Sign = "" },
        };

        using var content = new ByteArrayContent(req.ToByteArray());
        content.Headers.ContentType = new MediaTypeHeaderValue(HookProtobufHttp.ProtobufContentType);

        var resp = await client.PostAsync("/check-message", content);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var bytes = await resp.Content.ReadAsByteArrayAsync();
        var parsed = CheckMessageResponse.Parser.ParseFrom(bytes);
        Assert.False(parsed.Allow);
        Assert.Contains("sign", parsed.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MapMicsGetOfflineMessages_MissingSign_WhenRequired_ReturnsOkFalse()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        var app = builder.Build();

        app.MapMicsGetOfflineMessages(
            handler: (_, _) => new ValueTask<GetOfflineMessagesResponse>(new GetOfflineMessagesResponse { Ok = true }),
            options: new MicsHookMapOptions(_ => "secret", RequireSign: true));

        await app.StartAsync();
        var client = app.GetTestClient();

        var req = new GetOfflineMessagesRequest
        {
            Meta = new HookMeta { TenantId = "t1", RequestId = "r1", TimestampMs = 1, Sign = "" },
            UserId = "u1",
            DeviceId = "d1",
            MaxMessages = 100,
            Cursor = "",
        };

        using var content = new ByteArrayContent(req.ToByteArray());
        content.Headers.ContentType = new MediaTypeHeaderValue(HookProtobufHttp.ProtobufContentType);

        var resp = await client.PostAsync("/get-offline-messages", content);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var bytes = await resp.Content.ReadAsByteArrayAsync();
        var parsed = GetOfflineMessagesResponse.Parser.ParseFrom(bytes);
        Assert.False(parsed.Ok);
        Assert.Contains("sign", parsed.Reason, StringComparison.OrdinalIgnoreCase);
    }
}
