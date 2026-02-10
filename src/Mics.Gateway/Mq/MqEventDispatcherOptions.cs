namespace Mics.Gateway.Mq;

internal sealed record MqEventDispatcherOptions(
    int QueueCapacity,
    int MaxPendingPerTenant,
    int MaxAttempts,
    TimeSpan RetryBackoffBase,
    TimeSpan IdleDelay);
