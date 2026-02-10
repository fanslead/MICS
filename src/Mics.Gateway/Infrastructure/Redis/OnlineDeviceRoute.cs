namespace Mics.Gateway.Infrastructure.Redis;

internal sealed record OnlineDeviceRoute(
    string NodeId,
    string Endpoint,
    string ConnectionId,
    long OnlineAtUnixMs);

