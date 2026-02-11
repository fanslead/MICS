using Mics.Gateway.Infrastructure.Redis;

namespace Mics.Gateway.Group;

internal static class GroupFanoutPlanner
{
    public sealed record NodeBucket(string NodeId, string Endpoint, IReadOnlyList<string> UserIds);

    public static IReadOnlyList<NodeBucket> BuildBuckets(
        string selfNodeId,
        IReadOnlyList<string> members,
        IReadOnlyDictionary<string, IReadOnlyList<OnlineDeviceRoute>> routesByUser)
    {
        var buckets = new Dictionary<string, (string Endpoint, HashSet<string> Users)>(StringComparer.Ordinal);

        foreach (var userId in members)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                continue;
            }

            if (!routesByUser.TryGetValue(userId, out var routes) || routes.Count == 0)
            {
                continue;
            }

            foreach (var nodeRoute in routes)
            {
                if (!buckets.TryGetValue(nodeRoute.NodeId, out var bucket))
                {
                    bucket = (nodeRoute.Endpoint, new HashSet<string>(StringComparer.Ordinal));
                    buckets[nodeRoute.NodeId] = bucket;
                }

                bucket.Users.Add(userId);
            }
        }

        return buckets
            .Select(kv =>
            {
                var (nodeId, value) = (kv.Key, kv.Value);
                return new NodeBucket(nodeId, value.Endpoint, value.Users.ToArray());
            })
            .OrderBy(b => b.NodeId == selfNodeId ? "" : b.NodeId, StringComparer.Ordinal)
            .ToArray();
    }
}
