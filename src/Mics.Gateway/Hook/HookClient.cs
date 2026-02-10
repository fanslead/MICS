using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Headers;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using Mics.Contracts.Hook.V1;
using Mics.Contracts.Message.V1;
using Mics.Gateway.Metrics;
using Mics.Gateway.Security;

namespace Mics.Gateway.Hook;

internal sealed record AuthResult(bool Ok, string UserId, string DeviceId, TenantRuntimeConfig? Config, string Reason);

internal sealed record CheckMessageResult(bool Allow, bool Degraded, string Reason);

internal sealed record GroupMembersResult(bool Ok, bool Degraded, string Reason, IReadOnlyList<string> UserIds);

internal interface IHookClient
{
    ValueTask<AuthResult> AuthAsync(string authHookBaseUrl, string tenantId, string token, string deviceId, CancellationToken cancellationToken);
    ValueTask<CheckMessageResult> CheckMessageAsync(TenantRuntimeConfig tenantConfig, string tenantId, MessageRequest message, CancellationToken cancellationToken);
    ValueTask<GroupMembersResult> GetGroupMembersAsync(TenantRuntimeConfig tenantConfig, string tenantId, string groupId, CancellationToken cancellationToken);
}

internal sealed class HookClient : IHookClient
{
    private sealed class HookLogLimiter
    {
        private readonly ConcurrentDictionary<(string TenantId, HookOperation Op, string Result), long> _lastLogUnixMs = new();
        private readonly TimeProvider _timeProvider;
        private readonly long _minIntervalMs;

        public HookLogLimiter(TimeProvider timeProvider, TimeSpan minInterval)
        {
            _timeProvider = timeProvider;
            _minIntervalMs = (long)Math.Max(0, minInterval.TotalMilliseconds);
        }

        public bool ShouldLog(string tenantId, HookOperation op, string result)
        {
            if (_minIntervalMs <= 0)
            {
                return true;
            }

            var now = _timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
            var key = (tenantId, op, result);

            var last = _lastLogUnixMs.GetOrAdd(key, _ => long.MinValue);
            if (last != long.MinValue && now - last < _minIntervalMs)
            {
                return false;
            }

            _lastLogUnixMs[key] = now;
            return true;
        }
    }

    private enum HookPostOutcome
    {
        Ok = 0,
        QueueRejected = 1,
        Timeout = 2,
        Canceled = 3,
        Http4xx = 4,
        Http5xx = 5,
        HttpOther = 6,
        NetworkError = 7,
        DecodeError = 8,
    }

    private readonly record struct HookPostResult<TResponse>(HookPostOutcome Outcome, TResponse? Response);

    private readonly HttpClient _http;
    private readonly TimeSpan _timeout;
    private readonly HookCircuitBreaker _breaker;
    private readonly IHookMetaFactory _metaFactory;
    private readonly IAuthHookSecretProvider _authSecrets;
    private readonly ITenantHookPolicyCache _policies;
    private readonly IHookConcurrencyLimiter _concurrencyLimiter;
    private readonly MetricsRegistry _metrics;
    private readonly ILogger<HookClient> _logger;
    private readonly HookLogLimiter _logLimiter;

    public HookClient(
        HttpClient http,
        TimeSpan timeout,
        HookCircuitBreaker breaker,
        IHookMetaFactory metaFactory,
        IAuthHookSecretProvider authSecrets,
        ITenantHookPolicyCache policies,
        IHookConcurrencyLimiter concurrencyLimiter,
        MetricsRegistry metrics,
        ILogger<HookClient> logger,
        TimeProvider timeProvider)
    {
        _http = http;
        _timeout = timeout;
        _breaker = breaker;
        _metaFactory = metaFactory;
        _authSecrets = authSecrets;
        _policies = policies;
        _concurrencyLimiter = concurrencyLimiter;
        _metrics = metrics;
        _logger = logger;
        _logLimiter = new HookLogLimiter(timeProvider, TimeSpan.FromSeconds(5));
    }

    public async ValueTask<AuthResult> AuthAsync(string authHookBaseUrl, string tenantId, string token, string deviceId, CancellationToken cancellationToken)
    {
        var policy = _policies.Get(tenantId);

        if (!_breaker.TryBegin(tenantId, HookOperation.Auth))
        {
            RecordFailure(tenantId, HookOperation.Auth, result: "circuit_open", url: authHookBaseUrl, requestId: "");
            return new AuthResult(false, "", "", null, "auth hook circuit open");
        }

        var meta = _metaFactory.Create(tenantId);
        var request = new AuthRequest
        {
            Meta = meta,
            Token = token,
            DeviceId = deviceId,
        };

        if (_authSecrets.TryGet(tenantId, out var secret))
        {
            request.Meta.Sign = HmacSign.ComputeBase64(secret, request.Meta, PayloadForSign(request));
        }
        else if (policy.SignRequired)
        {
            RecordFailure(tenantId, HookOperation.Auth, result: "sign_required", url: authHookBaseUrl, requestId: request.Meta.RequestId);
            _breaker.OnFailure(tenantId, HookOperation.Auth, policy.Breaker);
            return new AuthResult(false, "", "", null, "auth hook sign required");
        }

        var authUrl = authHookBaseUrl.TrimEnd('/') + "/auth";
        var post = await PostAsync(authUrl, HookOperation.Auth, tenantId, policy.Acquire, request, AuthResponse.Parser, cancellationToken);
        if (post.Response is null)
        {
            if (ShouldCountFailureForBreaker(post.Outcome))
            {
                _breaker.OnFailure(tenantId, HookOperation.Auth, policy.Breaker);
            }

            RecordFailure(tenantId, HookOperation.Auth, ResultLabel(post.Outcome), authUrl, request.Meta.RequestId);
            return post.Outcome switch
            {
                HookPostOutcome.QueueRejected => new AuthResult(false, "", "", null, "hook queue rejected"),
                HookPostOutcome.Canceled => new AuthResult(false, "", "", null, "canceled"),
                _ => new AuthResult(false, "", "", null, "hook timeout/failure"),
            };
        }

        _breaker.OnSuccess(tenantId, HookOperation.Auth);
        if (post.Response.Ok && post.Response.Config is not null)
        {
            _policies.Update(tenantId, post.Response.Config);
        }
        return new AuthResult(post.Response.Ok, post.Response.UserId, post.Response.DeviceId, post.Response.Config, post.Response.Reason);
    }

    public async ValueTask<CheckMessageResult> CheckMessageAsync(TenantRuntimeConfig tenantConfig, string tenantId, MessageRequest message, CancellationToken cancellationToken)
    {
        var policy = _policies.Resolve(tenantId, tenantConfig);

        if (!_breaker.TryBegin(tenantId, HookOperation.CheckMessage))
        {
            RecordFailure(tenantId, HookOperation.CheckMessage, result: "circuit_open", url: tenantConfig.HookBaseUrl, requestId: "");
            return new CheckMessageResult(true, true, "hook circuit open");
        }

        var meta = _metaFactory.Create(tenantId);
        var request = new CheckMessageRequest
        {
            Meta = meta,
            Message = message,
        };

        if (!string.IsNullOrWhiteSpace(tenantConfig.TenantSecret))
        {
            request.Meta.Sign = HmacSign.ComputeBase64(tenantConfig.TenantSecret, request.Meta, PayloadForSign(request));
        }
        else if (policy.SignRequired)
        {
            RecordFailure(tenantId, HookOperation.CheckMessage, result: "sign_required", url: tenantConfig.HookBaseUrl, requestId: request.Meta.RequestId);
            _breaker.OnFailure(tenantId, HookOperation.CheckMessage, policy.Breaker);
            return new CheckMessageResult(false, false, "hook sign required");
        }

        var url = tenantConfig.HookBaseUrl.TrimEnd('/') + "/check-message";
        var post = await PostAsync(url, HookOperation.CheckMessage, tenantId, policy.Acquire, request, CheckMessageResponse.Parser, cancellationToken);
        if (post.Response is null)
        {
            if (ShouldCountFailureForBreaker(post.Outcome))
            {
                _breaker.OnFailure(tenantId, HookOperation.CheckMessage, policy.Breaker);
            }

            RecordFailure(tenantId, HookOperation.CheckMessage, ResultLabel(post.Outcome), url, request.Meta.RequestId);
            return post.Outcome switch
            {
                HookPostOutcome.QueueRejected => new CheckMessageResult(true, true, "hook queue rejected"),
                HookPostOutcome.Canceled => new CheckMessageResult(true, true, "canceled"),
                _ => new CheckMessageResult(true, true, "hook degraded"),
            };
        }

        _breaker.OnSuccess(tenantId, HookOperation.CheckMessage);
        return new CheckMessageResult(post.Response.Allow, false, post.Response.Reason);
    }

    public async ValueTask<GroupMembersResult> GetGroupMembersAsync(TenantRuntimeConfig tenantConfig, string tenantId, string groupId, CancellationToken cancellationToken)
    {
        var policy = _policies.Resolve(tenantId, tenantConfig);

        if (!_breaker.TryBegin(tenantId, HookOperation.GetGroupMembers))
        {
            RecordFailure(tenantId, HookOperation.GetGroupMembers, result: "circuit_open", url: tenantConfig.HookBaseUrl, requestId: "");
            return new GroupMembersResult(false, true, "hook circuit open", Array.Empty<string>());
        }

        var meta = _metaFactory.Create(tenantId);
        var request = new GetGroupMembersRequest
        {
            Meta = meta,
            GroupId = groupId,
        };

        if (!string.IsNullOrWhiteSpace(tenantConfig.TenantSecret))
        {
            request.Meta.Sign = HmacSign.ComputeBase64(tenantConfig.TenantSecret, request.Meta, PayloadForSign(request));
        }
        else if (policy.SignRequired)
        {
            RecordFailure(tenantId, HookOperation.GetGroupMembers, result: "sign_required", url: tenantConfig.HookBaseUrl, requestId: request.Meta.RequestId);
            _breaker.OnFailure(tenantId, HookOperation.GetGroupMembers, policy.Breaker);
            return new GroupMembersResult(false, false, "hook sign required", Array.Empty<string>());
        }

        var url = tenantConfig.HookBaseUrl.TrimEnd('/') + "/get-group-members";
        var post = await PostAsync(url, HookOperation.GetGroupMembers, tenantId, policy.Acquire, request, GetGroupMembersResponse.Parser, cancellationToken);
        if (post.Response is null)
        {
            if (ShouldCountFailureForBreaker(post.Outcome))
            {
                _breaker.OnFailure(tenantId, HookOperation.GetGroupMembers, policy.Breaker);
            }

            RecordFailure(tenantId, HookOperation.GetGroupMembers, ResultLabel(post.Outcome), url, request.Meta.RequestId);
            return post.Outcome switch
            {
                HookPostOutcome.QueueRejected => new GroupMembersResult(false, true, "hook queue rejected", Array.Empty<string>()),
                HookPostOutcome.Canceled => new GroupMembersResult(false, true, "canceled", Array.Empty<string>()),
                _ => new GroupMembersResult(false, true, "hook degraded", Array.Empty<string>()),
            };
        }

        _breaker.OnSuccess(tenantId, HookOperation.GetGroupMembers);
        return new GroupMembersResult(true, false, "", post.Response.UserIds);
    }

    private async ValueTask<HookPostResult<TResponse>> PostAsync<TRequest, TResponse>(
        string url,
        HookOperation op,
        string tenantId,
        HookAcquirePolicy acquirePolicy,
        TRequest request,
        MessageParser<TResponse> parser,
        CancellationToken cancellationToken)
        where TRequest : IMessage<TRequest>
        where TResponse : IMessage<TResponse>
    {
        await using var lease = await _concurrencyLimiter.TryAcquireAsync(tenantId, op, acquirePolicy, cancellationToken);
        if (lease is null)
        {
            var result = ResultLabel(HookPostOutcome.QueueRejected);
            _metrics.CounterInc("mics_hook_requests_total", 1, ("tenant", tenantId), ("op", op.ToString()), ("result", result));
            return new HookPostResult<TResponse>(HookPostOutcome.QueueRejected, default);
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_timeout);

        var startedAt = Stopwatch.GetTimestamp();

        var bytes = request.ToByteArray();
        using var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/protobuf");

        try
        {
            using var resp = await _http.PostAsync(url, content, cts.Token);
            if (!resp.IsSuccessStatusCode)
            {
                var status = (int)resp.StatusCode;
                var outcome = (status / 100) switch
                {
                    4 => HookPostOutcome.Http4xx,
                    5 => HookPostOutcome.Http5xx,
                    _ => HookPostOutcome.HttpOther,
                };

                var result = ResultLabel(outcome);
                _metrics.CounterInc("mics_hook_requests_total", 1, ("tenant", tenantId), ("op", op.ToString()), ("result", result));
                return new HookPostResult<TResponse>(outcome, default);
            }

            var respBytes = await resp.Content.ReadAsByteArrayAsync(cts.Token);
            TResponse parsed;
            try
            {
                parsed = parser.ParseFrom(respBytes);
            }
            catch (InvalidProtocolBufferException)
            {
                var result = ResultLabel(HookPostOutcome.DecodeError);
                _metrics.CounterInc("mics_hook_requests_total", 1, ("tenant", tenantId), ("op", op.ToString()), ("result", result));
                return new HookPostResult<TResponse>(HookPostOutcome.DecodeError, default);
            }

            _metrics.CounterInc("mics_hook_requests_total", 1, ("tenant", tenantId), ("op", op.ToString()), ("result", ResultLabel(HookPostOutcome.Ok)));
            return new HookPostResult<TResponse>(HookPostOutcome.Ok, parsed);
        }
        catch (OperationCanceledException)
        {
            var outcome = cancellationToken.IsCancellationRequested ? HookPostOutcome.Canceled : HookPostOutcome.Timeout;
            var result = ResultLabel(outcome);
            _metrics.CounterInc("mics_hook_requests_total", 1, ("tenant", tenantId), ("op", op.ToString()), ("result", result));
            return new HookPostResult<TResponse>(outcome, default);
        }
        catch (HttpRequestException)
        {
            var result = ResultLabel(HookPostOutcome.NetworkError);
            _metrics.CounterInc("mics_hook_requests_total", 1, ("tenant", tenantId), ("op", op.ToString()), ("result", result));
            return new HookPostResult<TResponse>(HookPostOutcome.NetworkError, default);
        }
        finally
        {
            var elapsed = Stopwatch.GetElapsedTime(startedAt);
            var ms = (long)elapsed.TotalMilliseconds;
            _metrics.CounterInc("mics_hook_duration_ms_total", ms, ("tenant", tenantId), ("op", op.ToString()));
            _metrics.CounterInc("mics_hook_duration_ms_count", 1, ("tenant", tenantId), ("op", op.ToString()));
        }
    }

    private static bool ShouldCountFailureForBreaker(HookPostOutcome outcome) =>
        outcome is HookPostOutcome.Timeout
            or HookPostOutcome.Http4xx
            or HookPostOutcome.Http5xx
            or HookPostOutcome.HttpOther
            or HookPostOutcome.NetworkError
            or HookPostOutcome.DecodeError;

    private static string ResultLabel(HookPostOutcome outcome) =>
        outcome switch
        {
            HookPostOutcome.Ok => "ok",
            HookPostOutcome.QueueRejected => "queue_rejected",
            HookPostOutcome.Timeout => "timeout",
            HookPostOutcome.Canceled => "canceled",
            HookPostOutcome.Http4xx => "http_4xx",
            HookPostOutcome.Http5xx => "http_5xx",
            HookPostOutcome.HttpOther => "http_other",
            HookPostOutcome.NetworkError => "network_error",
            HookPostOutcome.DecodeError => "decode_error",
            _ => "unknown",
        };

    private void RecordFailure(string tenantId, HookOperation op, string result, string url, string requestId)
    {
        if (!_logLimiter.ShouldLog(tenantId, op, result))
        {
            return;
        }

        // Avoid logging headers/body/sign; only metadata.
        _logger.LogWarning(
            "hook_request_failed tenant={TenantId} op={Op} result={Result} url={Url} requestId={RequestId}",
            tenantId,
            op,
            result,
            url,
            requestId);
    }

    private static CheckMessageRequest PayloadForSign(CheckMessageRequest request)
    {
        var clone = request.Clone();
        if (clone.Meta is not null)
        {
            clone.Meta.Sign = "";
        }
        return clone;
    }

    private static GetGroupMembersRequest PayloadForSign(GetGroupMembersRequest request)
    {
        var clone = request.Clone();
        if (clone.Meta is not null)
        {
            clone.Meta.Sign = "";
        }
        return clone;
    }

    private static AuthRequest PayloadForSign(AuthRequest request)
    {
        var clone = request.Clone();
        if (clone.Meta is not null)
        {
            clone.Meta.Sign = "";
        }
        return clone;
    }
}
