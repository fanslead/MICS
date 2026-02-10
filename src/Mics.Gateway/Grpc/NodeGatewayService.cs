using Google.Protobuf;
using Grpc.Core;
using Mics.Contracts.Message.V1;
using Mics.Contracts.Node.V1;
using Mics.Gateway.Connections;
using Mics.Gateway.Metrics;
using Mics.Gateway.Offline;
using Mics.Gateway.Protocol;
using Microsoft.Extensions.Logging;

namespace Mics.Gateway.Grpc;

internal sealed class NodeGatewayService : NodeGateway.NodeGatewayBase
{
    private readonly IConnectionRegistry _connections;
    private readonly IOfflineBufferStore _offline;
    private readonly MetricsRegistry _metrics;
    private readonly ILogger<NodeGatewayService> _logger;

    public NodeGatewayService(IConnectionRegistry connections, IOfflineBufferStore offline, MetricsRegistry metrics, ILogger<NodeGatewayService> logger)
    {
        _connections = connections;
        _offline = offline;
        _metrics = metrics;
        _logger = logger;
    }

    public override async Task<ForwardAck> ForwardSingle(ForwardSingleRequest request, ServerCallContext context)
    {
        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["TenantId"] = request.TenantId,
            ["MsgId"] = request.Message?.MsgId ?? "",
        });

        var message = request.Message?.Clone() ?? new MessageRequest();
        message.TenantId = request.TenantId;

        var sessions = _connections.GetAllForUser(request.TenantId, request.ToUserId);
        var frame = new ServerFrame { Delivery = new MessageDelivery { Message = message } };
        using var bytes = PooledProtobufSerializer.Serialize(frame);

        foreach (var session in sessions)
        {
            if (session.Socket.State != System.Net.WebSockets.WebSocketState.Open)
            {
                continue;
            }

            await session.Socket.SendAsync(bytes.Memory, System.Net.WebSockets.WebSocketMessageType.Binary, true, context.CancellationToken);
        }

        _metrics.CounterInc("mics_deliveries_total", sessions.Count, ("tenant", request.TenantId), ("via", "grpc_in_single"));
        return new ForwardAck { Ok = true };
    }

    public override async Task<ForwardAck> ForwardBatch(ForwardBatchRequest request, ServerCallContext context)
    {
        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["TenantId"] = request.TenantId,
            ["MsgId"] = request.Message?.MsgId ?? "",
        });

        var message = request.Message?.Clone() ?? new MessageRequest();
        message.TenantId = request.TenantId;

        var frame = new ServerFrame { Delivery = new MessageDelivery { Message = message } };
        using var bytes = PooledProtobufSerializer.Serialize(frame);

        var deliveries = 0;
        foreach (var userId in request.ToUserIds)
        {
            var sessions = _connections.GetAllForUser(request.TenantId, userId);
            foreach (var session in sessions)
            {
                if (session.Socket.State != System.Net.WebSockets.WebSocketState.Open)
                {
                    continue;
                }

                await session.Socket.SendAsync(bytes.Memory, System.Net.WebSockets.WebSocketMessageType.Binary, true, context.CancellationToken);
                deliveries++;
            }
        }

        _metrics.CounterInc("mics_deliveries_total", deliveries, ("tenant", request.TenantId), ("via", "grpc_in_batch"));
        return new ForwardAck { Ok = true };
    }

    public override Task<ForwardAck> BufferOffline(BufferOfflineRequest request, ServerCallContext context)
    {
        var ttlSeconds = request.TtlSeconds > 0 ? request.TtlSeconds : 300;
        var ok = _offline.TryAdd(request.TenantId, request.ToUserId, request.ServerFrame.ToByteArray(), TimeSpan.FromSeconds(ttlSeconds));
        if (ok)
        {
            _metrics.CounterInc("mics_offline_buffered_total", 1, ("tenant", request.TenantId), ("via", "grpc_in"));
            return Task.FromResult(new ForwardAck { Ok = true });
        }

        _metrics.CounterInc("mics_offline_buffer_skipped_total", 1, ("tenant", request.TenantId), ("via", "grpc_in"));
        return Task.FromResult(new ForwardAck { Ok = false, Reason = "offline buffer full" });
    }

    public override Task<DrainOfflineResponse> DrainOffline(DrainOfflineRequest request, ServerCallContext context)
    {
        var frames = _offline.Drain(request.TenantId, request.UserId);
        var response = new DrainOfflineResponse();
        foreach (var frameBytes in frames)
        {
            response.ServerFrames.Add(ByteString.CopyFrom(frameBytes));
        }

        _metrics.CounterInc("mics_offline_drained_total", frames.Count, ("tenant", request.TenantId), ("node", "home"));
        return Task.FromResult(response);
    }
}
