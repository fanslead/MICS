using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Google.Protobuf;
using Mics.Contracts.Hook.V1;

var builder = WebApplication.CreateBuilder(args);

var secrets = LoadTenantSecrets(builder.Configuration);
var tenantPolicies = LoadTenantPolicies(builder.Configuration);

var app = builder.Build();

app.MapGet("/", () => Results.Text("MICS HookMock"));
app.MapGet("/healthz", () => Results.Text("ok"));
app.MapGet("/readyz", () => Results.Text("ok"));

app.MapPost("/auth", async (HttpContext ctx) =>
{
    var request = await ReadProtobufAsync(AuthRequest.Parser, ctx.Request);
    var tenantId = request.Meta?.TenantId ?? string.Empty;
    tenantPolicies.TryGetValue(tenantId, out var policyOverride);
    var signRequired = ResolveHookSignRequired(tenantId, tenantPolicies, builder.Configuration);

    if (!TryGetTenantSecret(secrets, tenantId, out var tenantSecret))
    {
        var response = new AuthResponse
        {
            Meta = EchoMeta(request.Meta),
            Ok = false,
            Reason = "unknown tenant"
        };
        await WriteProtobufAsync(response, ctx.Response);
        return;
    }

    if (signRequired && (request.Meta is null || string.IsNullOrWhiteSpace(request.Meta.Sign)))
    {
        var response = new AuthResponse
        {
            Meta = EchoMeta(request.Meta),
            Ok = false,
            Reason = "invalid sign"
        };
        await WriteProtobufAsync(response, ctx.Response);
        return;
    }

    if (!ValidateSignIfPresent(tenantSecret, request.Meta, AuthPayloadForSign(request)))
    {
        var response = new AuthResponse
        {
            Meta = EchoMeta(request.Meta),
            Ok = false,
            Reason = "invalid sign"
        };
        await WriteProtobufAsync(response, ctx.Response);
        return;
    }

    if (!TryParseUserId(request.Token, out var userId))
    {
        var response = new AuthResponse
        {
            Meta = EchoMeta(request.Meta),
            Ok = false,
            Reason = "invalid token"
        };
        await WriteProtobufAsync(response, ctx.Response);
        return;
    }

    var hookBaseUrl = builder.Configuration["HOOK_BASE_URL"] ?? "http://localhost:8081";

    var okResponse = new AuthResponse
    {
        Meta = EchoMeta(request.Meta),
        Ok = true,
        UserId = userId,
        DeviceId = request.DeviceId,
        Config = new TenantRuntimeConfig
        {
            HookBaseUrl = hookBaseUrl,
            HeartbeatTimeoutSeconds = 30,
            OfflineBufferTtlSeconds = 300,
            TenantMaxConnections = 100_000,
            UserMaxConnections = 8,
            TenantMaxMessageQps = 10_000,
            TenantSecret = tenantSecret
        }
    };

    ApplyHookPolicyOverrides(okResponse.Config, policyOverride, builder.Configuration);
    await WriteProtobufAsync(okResponse, ctx.Response);
});

app.MapPost("/check-message", async (HttpContext ctx) =>
{
    var request = await ReadProtobufAsync(CheckMessageRequest.Parser, ctx.Request);
    var tenantId = request.Meta?.TenantId ?? string.Empty;
    var signRequired = ResolveHookSignRequired(tenantId, tenantPolicies, builder.Configuration);

    if (!TryGetTenantSecret(secrets, tenantId, out var tenantSecret))
    {
        var response = new CheckMessageResponse
        {
            Meta = EchoMeta(request.Meta),
            Allow = false,
            Reason = "unknown tenant"
        };
        await WriteProtobufAsync(response, ctx.Response);
        return;
    }

    if (signRequired && (request.Meta is null || string.IsNullOrWhiteSpace(request.Meta.Sign)))
    {
        var response = new CheckMessageResponse
        {
            Meta = EchoMeta(request.Meta),
            Allow = false,
            Reason = "invalid sign"
        };
        await WriteProtobufAsync(response, ctx.Response);
        return;
    }

    if (!ValidateSignIfPresent(tenantSecret, request.Meta, CheckMessagePayloadForSign(request)))
    {
        var response = new CheckMessageResponse
        {
            Meta = EchoMeta(request.Meta),
            Allow = false,
            Reason = "invalid sign"
        };
        await WriteProtobufAsync(response, ctx.Response);
        return;
    }

    var allow = request.Message?.MsgBody is { Length: > 0 } && request.Message.MsgBody.Length <= 1024 * 64;
    var responseOk = new CheckMessageResponse
    {
        Meta = EchoMeta(request.Meta),
        Allow = allow,
        Reason = allow ? "" : "empty or too large message body"
    };
    await WriteProtobufAsync(responseOk, ctx.Response);
});

app.MapPost("/get-group-members", async (HttpContext ctx) =>
{
    var request = await ReadProtobufAsync(GetGroupMembersRequest.Parser, ctx.Request);
    var tenantId = request.Meta?.TenantId ?? string.Empty;
    var signRequired = ResolveHookSignRequired(tenantId, tenantPolicies, builder.Configuration);

    if (!TryGetTenantSecret(secrets, tenantId, out var tenantSecret))
    {
        var response = new GetGroupMembersResponse
        {
            Meta = EchoMeta(request.Meta),
        };
        await WriteProtobufAsync(response, ctx.Response);
        return;
    }

    if (signRequired && (request.Meta is null || string.IsNullOrWhiteSpace(request.Meta.Sign)))
    {
        var response = new GetGroupMembersResponse { Meta = EchoMeta(request.Meta) };
        await WriteProtobufAsync(response, ctx.Response);
        return;
    }

    if (!ValidateSignIfPresent(tenantSecret, request.Meta, GroupMembersPayloadForSign(request)))
    {
        var response = new GetGroupMembersResponse { Meta = EchoMeta(request.Meta) };
        await WriteProtobufAsync(response, ctx.Response);
        return;
    }

    // Demo：从配置中读取组成员列表（不在 HookMock 内缓存业务数据；仅用于联调）
    // HOOK_GROUP_MEMBERS 示例：{"group-1":["u1","u2","u3"]}
    var groupsJson = builder.Configuration["HOOK_GROUP_MEMBERS"];
    var userIds = TryGetGroupMembers(groupsJson, request.GroupId);

    var ok = new GetGroupMembersResponse
    {
        Meta = EchoMeta(request.Meta),
    };
    ok.UserIds.AddRange(userIds);

    await WriteProtobufAsync(ok, ctx.Response);
});

app.Run();

static HookMeta EchoMeta(HookMeta? meta) =>
    meta is null
        ? new HookMeta { TenantId = "", RequestId = "", TimestampMs = 0, Sign = "" }
        : new HookMeta { TenantId = meta.TenantId, RequestId = meta.RequestId, TimestampMs = meta.TimestampMs, Sign = meta.Sign };

static async ValueTask<T> ReadProtobufAsync<T>(Google.Protobuf.MessageParser<T> parser, HttpRequest request)
    where T : Google.Protobuf.IMessage<T>
{
    using var ms = new MemoryStream();
    await request.Body.CopyToAsync(ms, request.HttpContext.RequestAborted);
    return parser.ParseFrom(ms.ToArray());
}

static async ValueTask WriteProtobufAsync<T>(T message, HttpResponse response)
    where T : Google.Protobuf.IMessage<T>
{
    response.ContentType = "application/protobuf";
    await response.Body.WriteAsync(message.ToByteArray(), response.HttpContext.RequestAborted);
}

static bool TryParseUserId(string token, out string userId)
{
    const string prefix = "valid:";
    if (token.StartsWith(prefix, StringComparison.Ordinal))
    {
        userId = token[prefix.Length..];
        return !string.IsNullOrWhiteSpace(userId);
    }

    userId = "";
    return false;
}

static Dictionary<string, string> LoadTenantSecrets(IConfiguration config)
{
    var json = config["HOOK_TENANT_SECRETS"];
    if (string.IsNullOrWhiteSpace(json))
    {
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["t1"] = "dev-secret-t1",
        };
    }

    var map = JsonSerializer.Deserialize(json, HookMockJsonContext.Default.DictionaryStringString)
              ?? new Dictionary<string, string>(StringComparer.Ordinal);

    return new Dictionary<string, string>(map, StringComparer.Ordinal);
}

static Dictionary<string, TenantHookPolicyOverride> LoadTenantPolicies(IConfiguration config)
{
    var json = config["HOOK_TENANT_POLICIES"];
    if (string.IsNullOrWhiteSpace(json))
    {
        return new Dictionary<string, TenantHookPolicyOverride>(StringComparer.Ordinal);
    }

    var map = JsonSerializer.Deserialize(json, HookMockJsonContext.Default.DictionaryStringTenantHookPolicyOverride)
              ?? new Dictionary<string, TenantHookPolicyOverride>(StringComparer.Ordinal);

    return new Dictionary<string, TenantHookPolicyOverride>(map, StringComparer.Ordinal);
}

static bool TryGetTenantSecret(Dictionary<string, string> secrets, string tenantId, out string tenantSecret) =>
    secrets.TryGetValue(tenantId, out tenantSecret!) && !string.IsNullOrWhiteSpace(tenantSecret);

static void ApplyHookPolicyOverrides(TenantRuntimeConfig cfg, TenantHookPolicyOverride? policyOverride, IConfiguration config)
{
    if (policyOverride?.HookMaxConcurrency is int max && max > 0)
    {
        cfg.HookMaxConcurrency = max;
    }
    else if (TryGetInt(config, "HOOK_MAX_CONCURRENCY", out var globalMax) && globalMax > 0)
    {
        cfg.HookMaxConcurrency = globalMax;
    }

    if (policyOverride?.HookQueueTimeoutMs is int qt && qt >= 0)
    {
        cfg.HookQueueTimeoutMs = qt;
    }
    else if (TryGetInt(config, "HOOK_QUEUE_TIMEOUT_MS", out var globalQt) && globalQt >= 0)
    {
        cfg.HookQueueTimeoutMs = globalQt;
    }

    if (policyOverride?.HookBreakerFailureThreshold is int thr && thr > 0)
    {
        cfg.HookBreakerFailureThreshold = thr;
    }
    else if (TryGetInt(config, "HOOK_BREAKER_FAILURE_THRESHOLD", out var globalThr) && globalThr > 0)
    {
        cfg.HookBreakerFailureThreshold = globalThr;
    }

    if (policyOverride?.HookBreakerOpenMs is int openMs && openMs >= 0)
    {
        cfg.HookBreakerOpenMs = openMs;
    }
    else if (TryGetInt(config, "HOOK_BREAKER_OPEN_MS", out var globalOpenMs) && globalOpenMs >= 0)
    {
        cfg.HookBreakerOpenMs = globalOpenMs;
    }

    if (policyOverride?.HookSignRequired is bool sr)
    {
        cfg.HookSignRequired = sr;
    }
    else if (TryGetBool(config, "HOOK_SIGN_REQUIRED", out var globalSr))
    {
        cfg.HookSignRequired = globalSr;
    }
}

static bool TryGetInt(IConfiguration config, string key, out int value)
{
    value = 0;
    var raw = config[key];
    return !string.IsNullOrWhiteSpace(raw) && int.TryParse(raw, out value);
}

static bool TryGetBool(IConfiguration config, string key, out bool value)
{
    value = false;
    var raw = config[key];
    return !string.IsNullOrWhiteSpace(raw) && bool.TryParse(raw, out value);
}

static bool ResolveHookSignRequired(string tenantId, IReadOnlyDictionary<string, TenantHookPolicyOverride> tenantPolicies, IConfiguration config)
{
    if (tenantPolicies.TryGetValue(tenantId, out var policyOverride) && policyOverride.HookSignRequired is bool sr)
    {
        return sr;
    }

    return TryGetBool(config, "HOOK_SIGN_REQUIRED", out var global) && global;
}

static bool ValidateSignIfPresent<TPayload>(string tenantSecret, HookMeta? meta, TPayload payloadForSign)
    where TPayload : Google.Protobuf.IMessage<TPayload>
{
    if (meta is null || string.IsNullOrWhiteSpace(meta.Sign))
    {
        return true;
    }

    var requestIdBytes = Encoding.UTF8.GetBytes(meta.RequestId ?? "");
    var tsBytes = new byte[8];
    BinaryPrimitives.WriteInt64LittleEndian(tsBytes, meta.TimestampMs);

    using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(tenantSecret));
    var payloadBytes = payloadForSign.ToByteArray();
    hmac.TransformBlock(payloadBytes, 0, payloadBytes.Length, null, 0);
    hmac.TransformBlock(requestIdBytes, 0, requestIdBytes.Length, null, 0);
    hmac.TransformFinalBlock(tsBytes, 0, tsBytes.Length);

    var expected = hmac.Hash!;
    byte[] actual;
    try
    {
        actual = Convert.FromBase64String(meta.Sign);
    }
    catch (FormatException)
    {
        return false;
    }

    return CryptographicOperations.FixedTimeEquals(expected, actual);
}

static IReadOnlyList<string> TryGetGroupMembers(string? groupsJson, string groupId)
{
    if (string.IsNullOrWhiteSpace(groupsJson))
    {
        return Array.Empty<string>();
    }

    var groups = JsonSerializer.Deserialize(groupsJson, HookMockJsonContext.Default.DictionaryStringStringArray);
    if (groups is null || !groups.TryGetValue(groupId, out var members) || members is null)
    {
        return Array.Empty<string>();
    }

    return members;
}

static AuthRequest AuthPayloadForSign(AuthRequest request)
{
    var clone = request.Clone();
    if (clone.Meta is not null)
    {
        clone.Meta.Sign = "";
    }
    return clone;
}

static CheckMessageRequest CheckMessagePayloadForSign(CheckMessageRequest request)
{
    var clone = request.Clone();
    if (clone.Meta is not null)
    {
        clone.Meta.Sign = "";
    }
    return clone;
}

static GetGroupMembersRequest GroupMembersPayloadForSign(GetGroupMembersRequest request)
{
    var clone = request.Clone();
    if (clone.Meta is not null)
    {
        clone.Meta.Sign = "";
    }
    return clone;
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(Dictionary<string, string[]>))]
[JsonSerializable(typeof(Dictionary<string, TenantHookPolicyOverride>))]
internal partial class HookMockJsonContext : JsonSerializerContext;

internal sealed record TenantHookPolicyOverride(
    int? HookMaxConcurrency,
    int? HookQueueTimeoutMs,
    int? HookBreakerFailureThreshold,
    int? HookBreakerOpenMs,
    bool? HookSignRequired);
