namespace Mics.Gateway.Infrastructure.Redis;

internal static class RedisKeys
{
    public static string OnlineUserHash(string tenantId, string userId) => $"{tenantId}:online:{userId}";

    public static string NodeInfoHash(string nodeId) => $"nodes:{nodeId}";

    public const string NodesLiveZset = "nodes:live";

    public const string NodesKnownSet = "nodes:known";

    public static string NodeRoutesSet(string nodeId) => $"nodes:{nodeId}:routes";

    public static string TenantConnLeasesZset(string tenantId) => $"{tenantId}:conn_leases";

    public static string UserConnLeasesZset(string tenantId, string userId) => $"{tenantId}:user:{userId}:conn_leases";
}
