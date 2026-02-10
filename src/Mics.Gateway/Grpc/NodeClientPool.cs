using Grpc.Net.Client;
using Mics.Contracts.Node.V1;

namespace Mics.Gateway.Grpc;

internal interface INodeClientPool : IAsyncDisposable
{
    NodeGateway.NodeGatewayClient Get(string endpoint);
}

internal sealed class NodeClientPool : INodeClientPool
{
    private readonly Dictionary<string, GrpcChannel> _channels = new(StringComparer.Ordinal);
    private readonly object _lock = new();

    public NodeGateway.NodeGatewayClient Get(string endpoint)
    {
        lock (_lock)
        {
            if (!_channels.TryGetValue(endpoint, out var channel))
            {
                channel = GrpcChannel.ForAddress(endpoint);
                _channels[endpoint] = channel;
            }

            return new NodeGateway.NodeGatewayClient(channel);
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_lock)
        {
            foreach (var ch in _channels.Values)
            {
                ch.Dispose();
            }

            _channels.Clear();
        }

        return ValueTask.CompletedTask;
    }
}

