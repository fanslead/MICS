namespace Mics.Gateway.Infrastructure.Redis;

internal static class NodeRouteEntryCodec
{
    private const char Sep = '|';

    public static string Encode(string tenantId, string userId, string deviceId) =>
        string.Concat(tenantId, Sep, userId, Sep, deviceId);

    public static bool TryDecode(string? entry, out string tenantId, out string userId, out string deviceId)
    {
        tenantId = "";
        userId = "";
        deviceId = "";

        if (string.IsNullOrWhiteSpace(entry))
        {
            return false;
        }

        var parts = entry.Split(Sep);
        if (parts.Length != 3)
        {
            return false;
        }

        tenantId = parts[0];
        userId = parts[1];
        deviceId = parts[2];
        return !(string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(deviceId));
    }
}

