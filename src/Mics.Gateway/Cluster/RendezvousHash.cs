using System.Security.Cryptography;
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
            var score = Sha256Score(tenantId, userId, node.NodeId);
            if (bestNode is null || score > best)
            {
                best = score;
                bestNode = node.NodeId;
            }
        }

        return bestNode;
    }

    private static ulong Sha256Score(string tenantId, string userId, string nodeId)
    {
        var payload = string.Concat(tenantId, ":", userId, ":", nodeId);
        var bytes = Encoding.UTF8.GetBytes(payload);
        var hash = SHA256.HashData(bytes);
        return BitConverter.ToUInt64(hash, 0);
    }
}
