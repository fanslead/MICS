using System.Text.Json;
using Mics.Contracts.Hook.V1;
using Mics.HookSdk;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var secrets = LoadSecrets(app.Configuration);
var opts = new MicsHookMapOptions(
    TenantSecretProvider: tenantId => secrets.TryGetValue(tenantId, out var s) ? s : null,
    RequireSign: ReadBool(app.Configuration, "HOOK_SIGN_REQUIRED", defaultValue: true));

app.MapGet("/", () => Results.Text("MICS HookSample"));

app.MapMicsAuth(async (req, ct) =>
{
    if (!req.Token.StartsWith("valid:", StringComparison.Ordinal))
    {
        return new AuthResponse { Ok = false, Reason = "invalid token", DeviceId = req.DeviceId };
    }

    var userId = req.Token["valid:".Length..];
    return new AuthResponse
    {
        Ok = true,
        UserId = userId,
        DeviceId = req.DeviceId,
        Config = new TenantRuntimeConfig
        {
            HookBaseUrl = builder.Configuration["HOOK_BASE_URL"] ?? "http://localhost:8081",
            HeartbeatTimeoutSeconds = 30,
            OfflineBufferTtlSeconds = 300,
            TenantMaxConnections = 100_000,
            UserMaxConnections = 8,
            TenantMaxMessageQps = 10_000,
            TenantSecret = secrets.TryGetValue(req.Meta?.TenantId ?? "", out var s) ? s : ""
        }
    };
}, opts);

app.MapMicsCheckMessage((req, ct) =>
{
    var allow = req.Message?.MsgBody is { Length: > 0 } && req.Message.MsgBody.Length <= 64 * 1024;
    return new ValueTask<CheckMessageResponse>(new CheckMessageResponse { Allow = allow, Reason = allow ? "" : "invalid body" });
}, opts);

app.MapMicsGetGroupMembers((req, ct) =>
{
    var resp = new GetGroupMembersResponse();
    resp.UserIds.Add("u1");
    resp.UserIds.Add("u2");
    resp.UserIds.Add("u3");
    return new ValueTask<GetGroupMembersResponse>(resp);
}, opts);

app.Run();

static Dictionary<string, string> LoadSecrets(IConfiguration cfg)
{
    var json = cfg["HOOK_TENANT_SECRETS"];
    if (string.IsNullOrWhiteSpace(json))
    {
        return new Dictionary<string, string>(StringComparer.Ordinal) { ["t1"] = "dev-secret-t1" };
    }

    var map = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
    return map is null ? new Dictionary<string, string>(StringComparer.Ordinal) : new Dictionary<string, string>(map, StringComparer.Ordinal);
}

static bool ReadBool(IConfiguration cfg, string key, bool defaultValue)
{
    var raw = cfg[key];
    return string.IsNullOrWhiteSpace(raw) ? defaultValue : bool.TryParse(raw, out var v) ? v : defaultValue;
}

