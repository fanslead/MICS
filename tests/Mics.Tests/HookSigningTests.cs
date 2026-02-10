using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Google.Protobuf;
using Mics.Contracts.Hook.V1;
using Mics.Contracts.Message.V1;
using Mics.Gateway.Hook;
using Mics.Gateway.Metrics;
using Mics.Gateway.Security;
using Microsoft.Extensions.Logging.Abstractions;

namespace Mics.Tests;

public sealed class HookSigningTests
{
    private sealed class FixedMetaFactory : IHookMetaFactory
    {
        private readonly HookMeta _meta;

        public FixedMetaFactory(HookMeta meta)
        {
            _meta = meta;
        }

        public HookMeta Create(string tenantId)
        {
            var clone = _meta.Clone();
            clone.TenantId = tenantId;
            clone.Sign = "";
            return clone;
        }
    }

    private sealed class MapSecrets : IAuthHookSecretProvider
    {
        private readonly IReadOnlyDictionary<string, string> _map;

        public MapSecrets(IReadOnlyDictionary<string, string> map)
        {
            _map = map;
        }

        public bool TryGet(string tenantId, out string secret) =>
            _map.TryGetValue(tenantId, out secret!) && !string.IsNullOrWhiteSpace(secret);
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public required Func<HttpRequestMessage, Task<HttpResponseMessage>> OnSendAsync { get; init; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            OnSendAsync(request);
    }

    [Fact]
    public async Task Auth_IsSigned_WhenSecretPresent()
    {
        var meta = new HookMeta { TenantId = "", RequestId = "r1", TimestampMs = 123, Sign = "" };
        var metaFactory = new FixedMetaFactory(meta);
        var secrets = new MapSecrets(new Dictionary<string, string> { ["t1"] = "secret" });
        var breaker = new HookCircuitBreaker(TimeProvider.System);
        var policies = new TenantHookPolicyCache(new HookPolicyDefaults(
            MaxConcurrencyDefault: 8,
            TenantMaxConcurrencyFallback: new Dictionary<string, int>(),
            QueueTimeoutDefault: TimeSpan.FromSeconds(1),
            BreakerFailureThresholdDefault: 5,
            BreakerOpenDurationDefault: TimeSpan.FromSeconds(5),
            SignRequiredDefault: true));

        var handler = new CaptureHandler
        {
            OnSendAsync = async req =>
            {
                var body = await req.Content!.ReadAsByteArrayAsync();
                var parsed = AuthRequest.Parser.ParseFrom(body);
                Assert.Equal("t1", parsed.Meta.TenantId);
                Assert.False(string.IsNullOrWhiteSpace(parsed.Meta.Sign));

                var payload = parsed.Clone();
                payload.Meta.Sign = "";

                var expected = HmacSign.ComputeBase64("secret", parsed.Meta, payload);
                Assert.Equal(expected, parsed.Meta.Sign);

                var ok = new AuthResponse
                {
                    Meta = parsed.Meta,
                    Ok = true,
                    UserId = "u1",
                    DeviceId = parsed.DeviceId,
                    Config = new TenantRuntimeConfig { HookBaseUrl = "http://localhost:8081", TenantSecret = "secret" },
                };

                var resp = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(ok.ToByteArray()),
                };
                resp.Content.Headers.ContentType = new MediaTypeHeaderValue("application/protobuf");
                return resp;
            }
        };

        var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        var metrics = new MetricsRegistry();
        var limiter = new HookConcurrencyLimiter(metrics);
        var client = new HookClient(http, TimeSpan.FromMilliseconds(150), breaker, metaFactory, secrets, policies, limiter, metrics, NullLogger<HookClient>.Instance, TimeProvider.System);

        var res = await client.AuthAsync("http://hook", "t1", "valid:u1", "d1", CancellationToken.None);
        Assert.True(res.Ok);
        Assert.Equal("u1", res.UserId);
    }

    [Fact]
    public async Task Auth_Fails_WhenSignRequiredButMissingSecret()
    {
        var metaFactory = new FixedMetaFactory(new HookMeta { TenantId = "", RequestId = "r1", TimestampMs = 123, Sign = "" });
        var secrets = new MapSecrets(new Dictionary<string, string>());
        var breaker = new HookCircuitBreaker(TimeProvider.System);
        var policies = new TenantHookPolicyCache(new HookPolicyDefaults(
            MaxConcurrencyDefault: 8,
            TenantMaxConcurrencyFallback: new Dictionary<string, int>(),
            QueueTimeoutDefault: TimeSpan.FromSeconds(1),
            BreakerFailureThresholdDefault: 5,
            BreakerOpenDurationDefault: TimeSpan.FromSeconds(5),
            SignRequiredDefault: true));
        var http = new HttpClient(new CaptureHandler
        {
            OnSendAsync = _ => throw new Exception("should not send")
        })
        { Timeout = Timeout.InfiniteTimeSpan };

        var metrics = new MetricsRegistry();
        var limiter = new HookConcurrencyLimiter(metrics);
        var client = new HookClient(http, TimeSpan.FromMilliseconds(150), breaker, metaFactory, secrets, policies, limiter, metrics, NullLogger<HookClient>.Instance, TimeProvider.System);
        var res = await client.AuthAsync("http://hook", "t1", "valid:u1", "d1", CancellationToken.None);
        Assert.False(res.Ok);
        Assert.Contains("sign required", res.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CheckMessage_Denies_WhenSignRequiredButMissingTenantSecret()
    {
        var metaFactory = new FixedMetaFactory(new HookMeta { TenantId = "", RequestId = "r1", TimestampMs = 123, Sign = "" });
        var secrets = new MapSecrets(new Dictionary<string, string>());
        var breaker = new HookCircuitBreaker(TimeProvider.System);
        var policies = new TenantHookPolicyCache(new HookPolicyDefaults(
            MaxConcurrencyDefault: 8,
            TenantMaxConcurrencyFallback: new Dictionary<string, int>(),
            QueueTimeoutDefault: TimeSpan.FromSeconds(1),
            BreakerFailureThresholdDefault: 5,
            BreakerOpenDurationDefault: TimeSpan.FromSeconds(5),
            SignRequiredDefault: true));

        var http = new HttpClient(new CaptureHandler
        {
            OnSendAsync = _ => throw new Exception("should not send")
        })
        { Timeout = Timeout.InfiniteTimeSpan };

        var metrics = new MetricsRegistry();
        var limiter = new HookConcurrencyLimiter(metrics);
        var client = new HookClient(http, TimeSpan.FromMilliseconds(150), breaker, metaFactory, secrets, policies, limiter, metrics, NullLogger<HookClient>.Instance, TimeProvider.System);

        var cfg = new TenantRuntimeConfig { HookBaseUrl = "http://localhost:8081", TenantSecret = "" };
        var msg = new MessageRequest
        {
            TenantId = "t1",
            UserId = "u1",
            DeviceId = "d1",
            MsgId = "m1",
            MsgType = MessageType.SingleChat,
            ToUserId = "u2",
            MsgBody = ByteString.CopyFrom(new byte[] { 1 }),
            TimestampMs = 1,
        };

        var res = await client.CheckMessageAsync(cfg, "t1", msg, CancellationToken.None);
        Assert.False(res.Allow);
        Assert.False(res.Degraded);
        Assert.Contains("sign required", res.Reason, StringComparison.OrdinalIgnoreCase);
    }
}
