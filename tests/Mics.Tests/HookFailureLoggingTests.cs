using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using Mics.Contracts.Hook.V1;
using Mics.Contracts.Message.V1;
using Mics.Gateway.Hook;
using Mics.Gateway.Metrics;

namespace Mics.Tests;

public sealed class HookFailureLoggingTests
{
    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _now;

        public ManualTimeProvider(DateTimeOffset now)
        {
            _now = now;
        }

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan delta) => _now = _now.Add(delta);
    }

    private sealed class NoSecrets : IAuthHookSecretProvider
    {
        public bool TryGet(string tenantId, out string secret)
        {
            secret = "";
            return false;
        }
    }

    private sealed class FixedMetaFactory : IHookMetaFactory
    {
        public HookMeta Create(string tenantId) => new HookMeta { TenantId = tenantId, RequestId = "r1", TimestampMs = 1, Sign = "" };
    }

    private sealed class FailingHandler : HttpMessageHandler
    {
        public required HttpStatusCode StatusCode { get; init; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var resp = new HttpResponseMessage(StatusCode)
            {
                Content = new ByteArrayContent(new byte[] { 1, 2, 3 }),
            };
            resp.Content.Headers.ContentType = new MediaTypeHeaderValue("application/protobuf");
            return Task.FromResult(resp);
        }
    }

    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception)));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }

    [Fact]
    public async Task FailureLogs_AreRateLimited()
    {
        var metrics = new MetricsRegistry();
        var limiter = new HookConcurrencyLimiter(metrics);
        var policies = new TenantHookPolicyCache(new HookPolicyDefaults(
            MaxConcurrencyDefault: 8,
            TenantMaxConcurrencyFallback: new Dictionary<string, int>(),
            QueueTimeoutDefault: TimeSpan.FromSeconds(1),
            BreakerFailureThresholdDefault: 100,
            BreakerOpenDurationDefault: TimeSpan.FromSeconds(1),
            SignRequiredDefault: false));

        var time = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        var logger = new ListLogger<HookClient>();

        var http = new HttpClient(new FailingHandler { StatusCode = HttpStatusCode.InternalServerError })
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };

        var client = new HookClient(
            http,
            timeout: TimeSpan.FromMilliseconds(50),
            breaker: new HookCircuitBreaker(time),
            metaFactory: new FixedMetaFactory(),
            authSecrets: new NoSecrets(),
            policies: policies,
            concurrencyLimiter: limiter,
            metrics: metrics,
            logger: logger,
            timeProvider: time);

        var cfg = new TenantRuntimeConfig { HookBaseUrl = "http://hook", TenantSecret = "" };
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

        _ = await client.CheckMessageAsync(cfg, "t1", msg, CancellationToken.None);
        _ = await client.CheckMessageAsync(cfg, "t1", msg, CancellationToken.None);

        Assert.Single(logger.Entries, e => e.Level == LogLevel.Warning);

        time.Advance(TimeSpan.FromSeconds(6));
        _ = await client.CheckMessageAsync(cfg, "t1", msg, CancellationToken.None);

        Assert.Equal(2, logger.Entries.Count(e => e.Level == LogLevel.Warning));
    }
}
