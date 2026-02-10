using StackExchange.Redis;

namespace Mics.Gateway.Infrastructure.Redis;

internal sealed record NodeInfo(string NodeId, string Endpoint);

internal interface INodeDirectory
{
    ValueTask RegisterSelfAsync(string nodeId, string endpoint, TimeSpan ttl, CancellationToken cancellationToken);
    ValueTask<IReadOnlyList<NodeInfo>> GetLiveNodesAsync(CancellationToken cancellationToken);
}

internal sealed class NodeDirectory : INodeDirectory
{
    private readonly IDatabase _db;
    private readonly TimeProvider _timeProvider;

    public NodeDirectory(IConnectionMultiplexer mux)
    {
        _db = mux.GetDatabase();
        _timeProvider = TimeProvider.System;
    }

    public async ValueTask RegisterSelfAsync(string nodeId, string endpoint, TimeSpan ttl, CancellationToken cancellationToken)
    {
        var key = RedisKeys.NodeInfoHash(nodeId);
        var now = _timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
        var expiresAtMs = now + (long)ttl.TotalMilliseconds;

        cancellationToken.ThrowIfCancellationRequested();

        var batch = _db.CreateBatch();

        var t1 = batch.HashSetAsync(key, new[]
        {
            new HashEntry("nodeId", nodeId),
            new HashEntry("endpoint", endpoint),
            new HashEntry("heartbeatAtMs", now),
        });

        var t2 = batch.KeyExpireAsync(key, ttl);

        var t3 = batch.SortedSetAddAsync(RedisKeys.NodesLiveZset, nodeId, expiresAtMs);
        var t4 = batch.SortedSetRemoveRangeByScoreAsync(RedisKeys.NodesLiveZset, double.NegativeInfinity, now);
        var t5 = batch.SetAddAsync(RedisKeys.NodesKnownSet, nodeId);

        batch.Execute();
        await Task.WhenAll(t1, t2, t3, t4, t5);
    }

    public async ValueTask<IReadOnlyList<NodeInfo>> GetLiveNodesAsync(CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow().ToUnixTimeMilliseconds();

        cancellationToken.ThrowIfCancellationRequested();
        await _db.SortedSetRemoveRangeByScoreAsync(RedisKeys.NodesLiveZset, double.NegativeInfinity, now);

        cancellationToken.ThrowIfCancellationRequested();
        var nodeIds = await _db.SortedSetRangeByScoreAsync(RedisKeys.NodesLiveZset, now, double.PositiveInfinity);
        if (nodeIds.Length == 0)
        {
            return Array.Empty<NodeInfo>();
        }

        var tasks = new Task<RedisValue>[nodeIds.Length];
        for (var i = 0; i < nodeIds.Length; i++)
        {
            var nodeId = nodeIds[i].ToString();
            tasks[i] = _db.HashGetAsync(RedisKeys.NodeInfoHash(nodeId), "endpoint");
        }

        await Task.WhenAll(tasks);

        var result = new List<NodeInfo>(nodeIds.Length);
        for (var i = 0; i < nodeIds.Length; i++)
        {
            var endpoint = tasks[i].Result;
            if (!endpoint.HasValue)
            {
                continue;
            }

            result.Add(new NodeInfo(nodeIds[i].ToString(), endpoint.ToString()));
        }

        return result;
    }
}
