using System.Net.WebSockets;
using Grpc.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Mics.Contracts.Message.V1;
using Mics.Contracts.Node.V1;
using Mics.Gateway.Connections;
using Mics.Gateway.Grpc;
using Mics.Gateway.Metrics;
using Mics.Gateway.Offline;
using Google.Protobuf;

namespace Mics.Tests;

public sealed class GrpcNodeAuthTests
{
    [Fact]
    public async Task NodeGatewayService_RejectsRequests_WhenClusterTokenIsConfigured_AndHeaderMissing()
    {
        var prev = Environment.GetEnvironmentVariable("CLUSTER_GRPC_TOKEN");
        Environment.SetEnvironmentVariable("CLUSTER_GRPC_TOKEN", "secret");

        try
        {
            var svc = new NodeGatewayService(
                connections: new EmptyRegistry(),
                offline: new NoopOffline(),
                metrics: new MetricsRegistry(),
                logger: NullLogger<NodeGatewayService>.Instance);

            var req = new ForwardSingleRequest
            {
                TenantId = "t1",
                ToUserId = "u2",
                Message = new MessageRequest { MsgId = "m1", MsgType = MessageType.SingleChat, ToUserId = "u2" },
            };

            var ex = await Assert.ThrowsAsync<RpcException>(() => svc.ForwardSingle(req, new TestCallContext(new Metadata())));
            Assert.Equal(StatusCode.Unauthenticated, ex.StatusCode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLUSTER_GRPC_TOKEN", prev);
        }
    }

    [Fact]
    public async Task NodeGatewayService_AllowsRequests_WhenClusterTokenMatches()
    {
        var prev = Environment.GetEnvironmentVariable("CLUSTER_GRPC_TOKEN");
        Environment.SetEnvironmentVariable("CLUSTER_GRPC_TOKEN", "secret");

        try
        {
            var svc = new NodeGatewayService(
                connections: new EmptyRegistry(),
                offline: new NoopOffline(),
                metrics: new MetricsRegistry(),
                logger: NullLogger<NodeGatewayService>.Instance);

            var req = new ForwardSingleRequest
            {
                TenantId = "t1",
                ToUserId = "u2",
                Message = new MessageRequest { MsgId = "m1", MsgType = MessageType.SingleChat, ToUserId = "u2" },
            };

            var headers = new Metadata { { "x-mics-node-token", "secret" } };
            var ack = await svc.ForwardSingle(req, new TestCallContext(headers));
            Assert.True(ack.Ok);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLUSTER_GRPC_TOKEN", prev);
        }
    }

    private sealed class EmptyRegistry : IConnectionRegistry
    {
        public bool TryAdd(ConnectionSession session) => true;
        public bool TryRemove(string tenantId, string userId, string deviceId, out ConnectionSession? removed) { removed = null; return true; }
        public bool TryGet(string tenantId, string userId, string deviceId, out ConnectionSession? session) { session = null; return false; }
        public void CopyAllForUserTo(string tenantId, string userId, List<ConnectionSession> destination)
        {
            destination.Clear();
        }
        public void CopyAllSessionsTo(List<ConnectionSession> destination) => destination.Clear();
    }

    private sealed class NoopOffline : IOfflineBufferStore
    {
        public bool TryAdd(string tenantId, string userId, ByteString serverFrameBytes, TimeSpan ttl) => true;
        public IReadOnlyList<ByteString> Drain(string tenantId, string userId) => Array.Empty<ByteString>();
    }

    private sealed class TestCallContext : ServerCallContext
    {
        private readonly Metadata _headers;

        public TestCallContext(Metadata headers)
        {
            _headers = headers;
        }

        protected override string MethodCore => "test";
        protected override string HostCore => "localhost";
        protected override string PeerCore => "peer";
        protected override DateTime DeadlineCore => DateTime.UtcNow.AddMinutes(1);
        protected override Metadata RequestHeadersCore => _headers;
        protected override CancellationToken CancellationTokenCore => CancellationToken.None;
        protected override Metadata ResponseTrailersCore { get; } = new();
        protected override Status StatusCore { get; set; }
        protected override WriteOptions? WriteOptionsCore { get; set; }
        protected override AuthContext AuthContextCore => new AuthContext("anonymous", new Dictionary<string, List<AuthProperty>>());

        protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions? options) => throw new NotSupportedException();
        protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders) => Task.CompletedTask;
    }
}

