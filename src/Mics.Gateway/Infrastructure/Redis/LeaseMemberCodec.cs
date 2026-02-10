namespace Mics.Gateway.Infrastructure.Redis;

internal static class LeaseMemberCodec
{
    public static string TenantMember(string userId, string deviceId) => $"{userId}|{deviceId}";

    public static string UserMember(string deviceId) => deviceId;
}

