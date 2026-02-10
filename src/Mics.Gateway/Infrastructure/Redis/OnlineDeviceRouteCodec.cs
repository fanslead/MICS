namespace Mics.Gateway.Infrastructure.Redis;

internal static class OnlineDeviceRouteCodec
{
    private const char Sep = '|';

    public static string Encode(OnlineDeviceRoute route) =>
        string.Concat(route.NodeId, Sep, route.Endpoint, Sep, route.ConnectionId, Sep, route.OnlineAtUnixMs);

    public static bool TryDecode(string? value, out OnlineDeviceRoute? route)
    {
        route = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Split(Sep);
        if (parts.Length != 4)
        {
            return false;
        }

        if (!long.TryParse(parts[3], out var ts))
        {
            return false;
        }

        route = new OnlineDeviceRoute(parts[0], parts[1], parts[2], ts);
        return true;
    }
}

