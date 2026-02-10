using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Mics.Gateway.Infrastructure.Redis;

namespace Mics.Gateway.Cluster;

internal interface INodeSnapshot
{
    IReadOnlyList<NodeInfo> Current { get; }
}

internal sealed class NodeSnapshotService : BackgroundService, INodeSnapshot
{
    private readonly INodeDirectory _directory;
    private readonly ILogger<NodeSnapshotService> _logger;
    private readonly string _nodeId;
    private readonly string _publicEndpoint;
    private readonly TimeSpan _ttl;

    private volatile NodeInfo[] _snapshot = Array.Empty<NodeInfo>();

    public NodeSnapshotService(
        INodeDirectory directory,
        ILogger<NodeSnapshotService> logger,
        string nodeId,
        string publicEndpoint,
        TimeSpan ttl)
    {
        _directory = directory;
        _logger = logger;
        _nodeId = nodeId;
        _publicEndpoint = publicEndpoint;
        _ttl = ttl;
    }

    public IReadOnlyList<NodeInfo> Current => _snapshot;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var nextRegister = DateTimeOffset.UtcNow;
        var nextRefresh = DateTimeOffset.UtcNow;

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTimeOffset.UtcNow;

            if (now >= nextRegister)
            {
                try
                {
                    await _directory.RegisterSelfAsync(_nodeId, _publicEndpoint, _ttl, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "node register failed");
                }

                nextRegister = now.AddSeconds(Math.Max(1, _ttl.TotalSeconds / 3));
            }

            if (now >= nextRefresh)
            {
                try
                {
                    var nodes = await _directory.GetLiveNodesAsync(stoppingToken);
                    _snapshot = nodes.ToArray();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "node refresh failed");
                }

                nextRefresh = now.AddSeconds(3);
            }

            await Task.Delay(250, stoppingToken);
        }
    }
}
