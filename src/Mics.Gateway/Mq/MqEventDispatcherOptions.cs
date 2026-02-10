namespace Mics.Gateway.Mq;

internal sealed record MqEventDispatcherOptions(
    int QueueCapacity,
    int MaxAttempts,
    TimeSpan RetryBackoffBase,
    TimeSpan IdleDelay);

