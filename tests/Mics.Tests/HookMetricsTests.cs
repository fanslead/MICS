using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using Google.Protobuf;
using Microsoft.Extensions.Logging.Abstractions;
using Mics.Contracts.Hook.V1;
using Mics.Gateway.Hook;
using Mics.Gateway.Infrastructure;
using Mics.Gateway.Metrics;

namespace Mics.Tests;

public sealed class HookMetricsTests
{
    private sealed class NoSecrets : IAuthHookSecretProvider
    {
        public bool TryGet(string tenantId, out string secret)
        {
            secret = "";
            return false;
        }
    }

    private sealed class BlockingAuthHandler : HttpMessageHandler
    {
        private int _calls;
        private readonly TaskCompletionSource _firstStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseFirst = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Calls => Volatile.Read(ref _calls);
        public Task FirstStarted => _firstStarted.Task;
        public void ReleaseFirst() => _releaseFirst.TrySetResult();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref _calls);
            if (call == 1)
            {
                _firstStarted.TrySetResult();
                await _releaseFirst.Task.WaitAsync(cancellationToken);
            }

            var ok = new AuthResponse
            {
                Meta = new HookMeta { TenantId = "t1", RequestId = "r", TimestampMs = 1, Sign = "" },
                Ok = true,
                UserId = "u1",
                DeviceId = "d1",
                Config = new TenantRuntimeConfig { HookBaseUrl = "http://hook", TenantSecret = "" },
                Reason = "",
            };

            var resp = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(ok.ToByteArray()),
            };
            resp.Content.Headers.ContentType = new MediaTypeHeaderValue("application/protobuf");
            return resp;
        }
    }

    [Fact]
    public async Task Auth_QueueRejected_DoesNotTripCircuitBreaker()
    {
        var metrics = new MetricsRegistry();
        var handler = new BlockingAuthHandler();
        var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };

        var defaults = new HookPolicyDefaults(
            MaxConcurrencyDefault: 1,
            TenantMaxConcurrencyFallback: new Dictionary<string, int>(),
            QueueTimeoutDefault: TimeSpan.Zero,
            BreakerFailureThresholdDefault: 1,
            BreakerOpenDurationDefault: TimeSpan.FromSeconds(30),
            SignRequiredDefault: false);

        var policies = new TenantHookPolicyCache(defaults);
        var breaker = new HookCircuitBreaker(TimeProvider.System);
        var limiter = new HookConcurrencyLimiter(metrics);

        var client = new HookClient(
            http,
            timeout: TimeSpan.FromSeconds(5),
            breaker,
            new DefaultHookMetaFactory(TimeProvider.System, new TraceContext()),
            new NoSecrets(),
            policies,
            limiter,
            metrics,
            NullLogger<HookClient>.Instance,
            TimeProvider.System);

        var firstTask = client.AuthAsync("http://hook", "t1", "tok", "d1", CancellationToken.None).AsTask();
        await handler.FirstStarted;

        var second = await client.AuthAsync("http://hook", "t1", "tok", "d1", CancellationToken.None);
        Assert.False(second.Ok);
        Assert.Contains("queue", second.Reason, StringComparison.OrdinalIgnoreCase);

        handler.ReleaseFirst();
        var first = await firstTask;
        Assert.True(first.Ok);

        var third = await client.AuthAsync("http://hook", "t1", "tok", "d1", CancellationToken.None);
        Assert.True(third.Ok);

        Assert.Equal(2, handler.Calls);

        var text = metrics.CollectPrometheusText();
        Assert.Contains("mics_hook_requests_total{tenant=\"t1\",op=\"Auth\",result=\"queue_rejected\"} 1", text);
    }
}
