using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Mics.Gateway.Metrics;

namespace Mics.Gateway.Infrastructure.Redis;

internal sealed class AdmissionUnregisterRetryService : BackgroundService
{
    private readonly RedisConnectionAdmission _inner;
    private readonly AdmissionUnregisterRetryQueue _queue;
    private readonly MetricsRegistry _metrics;
    private readonly ILogger<AdmissionUnregisterRetryService> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly int _maxAttempts;
    private readonly TimeSpan _backoffBase;

    public AdmissionUnregisterRetryService(
        RedisConnectionAdmission inner,
        AdmissionUnregisterRetryQueue queue,
        MetricsRegistry metrics,
        ILogger<AdmissionUnregisterRetryService> logger,
        TimeProvider timeProvider,
        int maxAttempts,
        TimeSpan backoffBase)
    {
        _inner = inner;
        _queue = queue;
        _metrics = metrics;
        _logger = logger;
        _timeProvider = timeProvider;
        _maxAttempts = Math.Clamp(maxAttempts, 1, 10);
        _backoffBase = backoffBase < TimeSpan.Zero ? TimeSpan.Zero : backoffBase;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (await _queue.Reader.WaitToReadAsync(stoppingToken))
        {
            while (_queue.Reader.TryRead(out var item))
            {
                _queue.OnDequeued();
                await ProcessItemAsync(item, stoppingToken);
            }
        }
    }

    private async Task ProcessItemAsync(AdmissionUnregisterWorkItem item, CancellationToken cancellationToken)
    {
        var attempt = Math.Max(1, item.Attempt);
        for (; attempt <= _maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await _inner.UnregisterAsync(
                    item.TenantId,
                    item.UserId,
                    item.DeviceId,
                    item.ExpectedNodeId,
                    item.ExpectedConnectionId,
                    cancellationToken);

                _metrics.CounterInc("mics_admission_unregister_retry_ok_total", 1, ("tenant", item.TenantId));
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _metrics.CounterInc("mics_admission_unregister_retry_failed_total", 1, ("tenant", item.TenantId));
                _logger.LogWarning(ex, "admission_unregister_retry_failed tenant={TenantId} user={UserId} device={DeviceId} attempt={Attempt}", item.TenantId, item.UserId, item.DeviceId, attempt);

                if (attempt >= _maxAttempts)
                {
                    _metrics.CounterInc("mics_admission_unregister_retry_giveup_total", 1, ("tenant", item.TenantId));
                    return;
                }

                var delay = ComputeBackoff(attempt);
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, cancellationToken);
                }
            }
        }
    }

    private TimeSpan ComputeBackoff(int attempt)
    {
        if (_backoffBase <= TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        var factor = 1 << Math.Clamp(attempt - 1, 0, 10);
        var ms = Math.Min(5_000, (long)_backoffBase.TotalMilliseconds * factor);
        if (ms <= 0)
        {
            return TimeSpan.Zero;
        }

        // Add a small deterministic jitter to avoid herd effect.
        var jitter = (int)(_timeProvider.GetUtcNow().ToUnixTimeMilliseconds() % 17);
        return TimeSpan.FromMilliseconds(ms + jitter);
    }
}
