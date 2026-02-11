namespace Mics.Gateway.Mq;

internal sealed record MqEventDispatcherOptions(
    int QueueCapacity,
    int MaxPendingPerTenant,
    int MaxAttempts,
    TimeSpan RetryBackoffBase,
    TimeSpan IdleDelay,
    int DlqFallbackQueueCapacity = 10_000,
    int DlqFallbackMaxPendingPerTenant = 2_000,
    int DlqFallbackMaxAttempts = 20,
    TimeSpan DlqFallbackRetryBackoffBase = default);
