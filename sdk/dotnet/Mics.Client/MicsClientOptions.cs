namespace Mics.Client;

public sealed record MicsClientOptions(
    TimeSpan ConnectTimeout,
    TimeSpan AckTimeout,
    int MaxSendAttempts,
    TimeSpan HeartbeatInterval,
    bool AutoReconnect,
    TimeSpan ReconnectMinDelay,
    TimeSpan ReconnectMaxDelay,
    IMicsMessageCrypto? MessageCrypto)
{
    public static MicsClientOptions Default { get; } = new(
        ConnectTimeout: TimeSpan.FromSeconds(5),
        AckTimeout: TimeSpan.FromSeconds(3),
        MaxSendAttempts: 3,
        HeartbeatInterval: TimeSpan.FromSeconds(10),
        AutoReconnect: true,
        ReconnectMinDelay: TimeSpan.FromMilliseconds(200),
        ReconnectMaxDelay: TimeSpan.FromSeconds(5),
        MessageCrypto: null);
}
