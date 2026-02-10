using System.Text;
using Mics.Gateway.Infrastructure.Redis;

namespace Mics.Gateway.Cluster;

internal static class RendezvousHash
{
    public static string? PickNodeId(string tenantId, string userId, IReadOnlyList<NodeInfo> nodes)
    {
        if (nodes.Count == 0)
        {
            return null;
        }

        ulong best = 0;
        string? bestNode = null;

        foreach (var node in nodes)
        {
            var score = Fnva64(tenantId, userId, node.NodeId);
            if (bestNode is null || score > best)
            {
                best = score;
                bestNode = node.NodeId;
            }
        }

        return bestNode;
    }

    private static ulong Fnva64(string tenantId, string userId, string nodeId)
    {
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;

        var h = offset;
        h = Fnva64(h, tenantId, prime);
        h = Fnva64(h, ":", prime);
        h = Fnva64(h, userId, prime);
        h = Fnva64(h, ":", prime);
        h = Fnva64(h, nodeId, prime);
        return h;
    }

    private static ulong Fnva64(ulong hash, string value, ulong prime)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        foreach (var b in bytes)
        {
            hash ^= b;
            hash *= prime;
        }
        return hash;
    }
}
