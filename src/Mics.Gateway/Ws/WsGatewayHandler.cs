using System.Buffers;
using System.Net.WebSockets;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Mics.Contracts.Message.V1;
using Mics.Contracts.Node.V1;
using Mics.Gateway.Cluster;
using Mics.Gateway.Connections;
using Mics.Gateway.Grpc;
using Mics.Gateway.Hook;
using Mics.Gateway.Infrastructure;
using Mics.Gateway.Infrastructure.Redis;
using Mics.Gateway.Metrics;
using Mics.Gateway.Mq;
using Mics.Gateway.Offline;
using Mics.Gateway.Protocol;

namespace Mics.Gateway.Ws;

internal sealed class WsGatewayHandler
{
    private readonly string _nodeId;
    private readonly string _publicEndpoint;
    private readonly IReadOnlyDictionary<string, string> _tenantAuthMap;
    private readonly int _maxMessageBytes;
    private readonly int _groupRouteChunkSize;
    private readonly int _groupOfflineBufferMaxUsers;
    private readonly int _groupMembersMaxUsers;

    private readonly IHookClient _hook;
    private readonly IConnectionRegistry _connections;
    private readonly IOnlineRouteStore _routes;
    private readonly IConnectionAdmission _admission;
    private readonly INodeSnapshot _nodes;
    private readonly INodeClientPool _nodeClients;
    private readonly GrpcNodeCircuitBreaker _grpcBreaker;
    private readonly GrpcBreakerPolicy _grpcBreakerPolicy;
    private readonly IOfflineBufferStore _offline;
    private readonly IRedisRateLimiter _rateLimiter;
    private readonly IMessageDeduplicator _dedup;
    private readonly MqEventDispatcher _mq;
    private readonly MetricsRegistry _metrics;
    private readonly ILogger<WsGatewayHandler> _logger;
    private readonly ITraceContext _traceContext;
    private readonly IShutdownState _shutdown;

    private long _activeConnections;

    public WsGatewayHandler(
        string nodeId,
        string publicEndpoint,
        IReadOnlyDictionary<string, string> tenantAuthMap,
        IHookClient hook,
        IConnectionRegistry connections,
        IOnlineRouteStore routes,
        IConnectionAdmission admission,
        INodeSnapshot nodes,
        INodeClientPool nodeClients,
        GrpcNodeCircuitBreaker grpcBreaker,
        GrpcBreakerPolicy grpcBreakerPolicy,
        IOfflineBufferStore offline,
        IRedisRateLimiter rateLimiter,
        IMessageDeduplicator dedup,
        MqEventDispatcher mq,
        MetricsRegistry metrics,
        ILogger<WsGatewayHandler> logger,
        ITraceContext traceContext,
        IShutdownState shutdown,
        int maxMessageBytes,
        int groupRouteChunkSize,
        int groupOfflineBufferMaxUsers,
        int groupMembersMaxUsers)
    {
        _nodeId = nodeId;
        _publicEndpoint = publicEndpoint;
        _tenantAuthMap = tenantAuthMap;
        _maxMessageBytes = Math.Clamp(maxMessageBytes, 0, 64 * 1024 * 1024);
        _groupRouteChunkSize = Math.Clamp(groupRouteChunkSize, 1, 4096);
        _groupOfflineBufferMaxUsers = Math.Max(0, groupOfflineBufferMaxUsers);
        _groupMembersMaxUsers = Math.Clamp(groupMembersMaxUsers, 1, 5_000_000);
        _hook = hook;
        _connections = connections;
        _routes = routes;
        _admission = admission;
        _nodes = nodes;
        _nodeClients = nodeClients;
        _grpcBreaker = grpcBreaker;
        _grpcBreakerPolicy = grpcBreakerPolicy;
        _offline = offline;
        _rateLimiter = rateLimiter;
        _dedup = dedup;
        _mq = mq;
        _metrics = metrics;
        _logger = logger;
        _traceContext = traceContext;
        _shutdown = shutdown;
    }

    public async Task HandleAsync(HttpContext ctx)
    {
        if (_shutdown.IsDraining)
        {
            ctx.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return;
        }

        if (!ctx.WebSockets.IsWebSocketRequest)
        {
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var tenantId = ctx.Request.Query["tenantId"].ToString();
        var token = ctx.Request.Query["token"].ToString();
        var deviceId = ctx.Request.Query["deviceId"].ToString();

        using var socket = await ctx.WebSockets.AcceptWebSocketAsync();

        var traceId = Guid.NewGuid().ToString("N");
        var prevTrace = _traceContext.TraceId;
        _traceContext.TraceId = traceId;

        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["TenantId"] = tenantId,
            ["NodeId"] = _nodeId,
            ["TraceId"] = traceId,
        });

        try
        {
            if (string.IsNullOrWhiteSpace(tenantId) || !_tenantAuthMap.TryGetValue(tenantId, out var authBaseUrl))
            {
                await CloseAsync(socket, MicsProtocolCodes.CloseTenantInvalid, "invalid tenant", ctx.RequestAborted);
                return;
            }

            var auth = await _hook.AuthAsync(authBaseUrl, tenantId, token, deviceId, ctx.RequestAborted);
            if (!auth.Ok || auth.Config is null || string.IsNullOrWhiteSpace(auth.UserId))
            {
                await CloseAsync(socket, MicsProtocolCodes.CloseAuthFailed, auth.Reason, ctx.RequestAborted);
                return;
            }

            var userId = auth.UserId;
            var tenantCfg = auth.Config;

            var connectionId = Guid.NewGuid().ToString("N");
            var session = new ConnectionSession(tenantId, userId, deviceId, connectionId, traceId, socket, tenantCfg);
            if (!_connections.TryAdd(session))
            {
                await CloseAsync(socket, MicsProtocolCodes.CloseRateLimited, "duplicate device", ctx.RequestAborted);
                return;
            }

            var onlineAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var admission = await _admission.TryRegisterAsync(
                tenantId,
                userId,
                deviceId,
                new OnlineDeviceRoute(_nodeId, _publicEndpoint, connectionId, onlineAt),
                tenantCfg.HeartbeatTimeoutSeconds,
                tenantCfg.TenantMaxConnections,
                tenantCfg.UserMaxConnections,
                ctx.RequestAborted);

            if (!admission.Allowed)
            {
                _metrics.CounterInc("mics_rate_limited_total", 1, ("tenant", tenantId), ("kind", "connection_limit"));
                _connections.TryRemove(tenantId, userId, deviceId, out _);
                await CloseAsync(socket, MicsProtocolCodes.CloseRateLimited, admission.Reason, ctx.RequestAborted);
                return;
            }

            UpdateConnectionGauges(tenantId, 1);
            await SendFrameAsync(socket, new ServerFrame
            {
                ConnectAck = new ConnectAck
                {
                    Code = 1000,
                    TenantId = tenantId,
                    UserId = userId,
                    DeviceId = deviceId,
                    NodeId = _nodeId,
                    TraceId = traceId,
                }
            }, ctx.RequestAborted);

            _mq.TryEnqueue(MqEventFactory.CreateConnectOnline(tenantId, userId, deviceId, _nodeId, traceId, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), tenantCfg.TenantSecret));

            _metrics.CounterInc("mics_ws_connected_total", 1, ("tenant", tenantId), ("node", _nodeId));
            _logger.LogInformation("ws_connected user={UserId} device={DeviceId}", userId, deviceId);

            await DrainOfflineAsync(session, ctx.RequestAborted);

            try
            {
                await ReceiveLoopAsync(session, ctx.RequestAborted);
            }
            finally
            {
                _connections.TryRemove(tenantId, userId, deviceId, out _);
                await _admission.UnregisterAsync(tenantId, userId, deviceId, _nodeId, connectionId, CancellationToken.None);
                _mq.TryEnqueue(MqEventFactory.CreateConnectOffline(tenantId, userId, deviceId, _nodeId, traceId, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), session.TenantConfig.TenantSecret));
                _metrics.CounterInc("mics_ws_disconnected_total", 1, ("tenant", tenantId), ("node", _nodeId));
                UpdateConnectionGauges(tenantId, -1);
                _logger.LogInformation("ws_disconnected user={UserId} device={DeviceId}", userId, deviceId);
            }
        }
        finally
        {
            _traceContext.TraceId = prevTrace;
        }
    }

	    private async Task ReceiveLoopAsync(ConnectionSession session, CancellationToken cancellationToken)
	    {
	        var prevTrace = _traceContext.TraceId;
	        _traceContext.TraceId = session.TraceId;
	        try
	        {
	            var socket = session.Socket;

        while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            var maxFrameBytes = _maxMessageBytes > 0
                ? Math.Min(int.MaxValue, _maxMessageBytes + (64 * 1024))
                : 0;

            PooledProtobufBytes? binary;
            try
            {
                binary = await WebSocketMessageIO.ReadBinaryAsync(socket, maxFrameBytes, cancellationToken);
            }
            catch (WebSocketMessageIO.WebSocketFrameTooLargeException)
            {
                _metrics.CounterInc("mics_ws_frames_rejected_total", 1, ("tenant", session.TenantId), ("reason", "frame_too_large"));
                await SendFrameAsync(socket, new ServerFrame
                {
                    Error = new ServerError { Code = MicsProtocolCodes.ErrorFrameTooLarge, Message = "frame too large" }
                }, cancellationToken);
                continue;
            }

            if (binary is null)
            {
                return;
            }

	            ClientFrame frame;
	            try
	            {
	                var cis = new CodedInputStream(binary.Buffer, 0, binary.Length);
	                frame = ClientFrame.Parser.ParseFrom(cis);
	            }
	            catch
	            {
	                await SendFrameAsync(socket, new ServerFrame { Error = new ServerError { Code = MicsProtocolCodes.ErrorInvalidProtobuf, Message = "invalid protobuf" } }, cancellationToken);
	                continue;
	            }
	            finally
	            {
	                binary.Dispose();
	            }

            if (frame.PayloadCase != ClientFrame.PayloadOneofCase.Message)
            {
                if (frame.PayloadCase == ClientFrame.PayloadOneofCase.HeartbeatPing)
                {
                    session.Touch(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                    await SendFrameAsync(socket, new ServerFrame
                    {
                        HeartbeatPong = new HeartbeatPong { TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }
                    }, cancellationToken);
                }

                continue;
            }

            session.Touch(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            var msg = NormalizeIncomingMessage(session, frame.Message);

            if (_maxMessageBytes > 0 && msg.MsgBody.Length > _maxMessageBytes)
            {
                _metrics.CounterInc("mics_messages_rejected_total", 1, ("tenant", session.TenantId), ("reason", "msg_body_too_large"));
                await SendFrameAsync(socket, new ServerFrame
                {
                    Ack = new MessageAck
                    {
                        MsgId = msg.MsgId,
                        Status = AckStatus.Failed,
                        TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                        Reason = "msg_body_too_large"
                    }
                }, cancellationToken);
                continue;
            }

            if (!TryValidateMessage(msg, out var invalidReason))
            {
                _metrics.CounterInc("mics_messages_rejected_total", 1, ("tenant", session.TenantId), ("reason", invalidReason));
                await SendFrameAsync(socket, new ServerFrame
                {
                    Ack = new MessageAck
                    {
                        MsgId = msg.MsgId,
                        Status = AckStatus.Failed,
                        TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                        Reason = invalidReason
                    }
                }, cancellationToken);
                continue;
            }

            _metrics.CounterInc("mics_messages_in_total", 1, ("tenant", session.TenantId), ("type", msg.MsgType.ToString()));

            var dedupTtl = TimeSpan.FromMinutes(10);
            var isNew = await _dedup.TryMarkAsync(session.TenantId, msg.MsgId, dedupTtl, cancellationToken);
            if (!isNew)
            {
                _metrics.CounterInc("mics_dedup_hits_total", 1, ("tenant", session.TenantId));
                await SendFrameAsync(socket, new ServerFrame
                {
                    Ack = new MessageAck { MsgId = msg.MsgId, Status = AckStatus.Sent, TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), Reason = "duplicate" }
                }, cancellationToken);
                continue;
            }

            if (session.TenantConfig.TenantMaxMessageQps > 0)
            {
                var ok = await _rateLimiter.TryConsumeTenantMessageAsync(session.TenantId, session.TenantConfig.TenantMaxMessageQps, cancellationToken);
                if (!ok)
                {
                    _metrics.CounterInc("mics_rate_limited_total", 1, ("tenant", session.TenantId), ("kind", "message_qps"));
                    await SendFrameAsync(socket, new ServerFrame
                    {
                        Ack = new MessageAck { MsgId = msg.MsgId, Status = AckStatus.Failed, TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), Reason = "tenant qps limited" }
                    }, cancellationToken);
                    continue;
                }
            }

            if (msg.MsgType == MessageType.SingleChat)
            {
                // Per spec (3.4.2): do not wait for hook before starting Redis route lookup, but still gate delivery on hook allow.
                var routesTask = _routes.GetAllDevicesAsync(session.TenantId, msg.ToUserId, cancellationToken);
                var checkTask = _hook.CheckMessageAsync(session.TenantConfig, session.TenantId, msg, cancellationToken);

                var check = await checkTask;
                _metrics.CounterInc("mics_hook_check_message_total", 1, ("tenant", session.TenantId), ("result", check.Degraded ? "degraded" : check.Allow ? "allow" : "deny"));
                if (!check.Allow)
                {
                    _ = routesTask.AsTask().ContinueWith(static t => _ = t.Exception, TaskScheduler.Default);
                    await SendFrameAsync(socket, new ServerFrame
                    {
                        Ack = new MessageAck { MsgId = msg.MsgId, Status = AckStatus.Failed, TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), Reason = check.Reason }
                    }, cancellationToken);
                    continue;
                }

                try
                {
                    var routes = await routesTask;
                    var (delivered, deliveryReason) = await HandleSingleChatAsync(session, msg, routes, cancellationToken);
                    _mq.TryEnqueue(MqEventFactory.CreateForMessage(msg, _nodeId, session.TraceId, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), session.TenantConfig.TenantSecret));

                    await SendFrameAsync(socket, new ServerFrame
                    {
                        Ack = new MessageAck
                        {
                            MsgId = msg.MsgId,
                            Status = delivered ? AckStatus.Sent : AckStatus.Failed,
                            TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                            Reason = delivered ? "" : deliveryReason
                        }
                    }, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _metrics.CounterInc("mics_messages_failed_total", 1, ("tenant", session.TenantId));
                    _logger.LogWarning(ex, "ws_message_process_failed msg={MsgId}", msg.MsgId);
                    await SendFrameAsync(socket, new ServerFrame
                    {
                        Ack = new MessageAck
                        {
                            MsgId = msg.MsgId,
                            Status = AckStatus.Failed,
                            TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                            Reason = "internal error"
                        }
                    }, cancellationToken);
                }

                continue;
            }

            if (msg.MsgType == MessageType.GroupChat)
            {
                // GroupChat: start both hooks in parallel, but still gate fanout/delivery on CheckMessage allow.
                using var membersCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

                var checkTask = _hook.CheckMessageAsync(session.TenantConfig, session.TenantId, msg, cancellationToken);
                var membersTask = _hook.GetGroupMembersAsync(session.TenantConfig, session.TenantId, msg.GroupId, membersCts.Token);

                var check = await checkTask;
                _metrics.CounterInc("mics_hook_check_message_total", 1, ("tenant", session.TenantId), ("result", check.Degraded ? "degraded" : check.Allow ? "allow" : "deny"));
                if (!check.Allow)
                {
                    membersCts.Cancel();
                    _ = membersTask.AsTask().ContinueWith(static t => _ = t.Exception, TaskScheduler.Default);

                    await SendFrameAsync(socket, new ServerFrame
                    {
                        Ack = new MessageAck { MsgId = msg.MsgId, Status = AckStatus.Failed, TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), Reason = check.Reason }
                    }, cancellationToken);
                    continue;
                }

                try
                {
                    var members = await membersTask;
                    _metrics.CounterInc("mics_hook_get_group_members_total", 1, ("tenant", session.TenantId), ("result", members.Degraded ? "degraded" : members.Ok ? "ok" : "fail"));
                    if (!members.Ok)
                    {
                        await SendFrameAsync(socket, new ServerFrame
                        {
                            Ack = new MessageAck { MsgId = msg.MsgId, Status = AckStatus.Failed, TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), Reason = members.Reason }
                        }, cancellationToken);
                        continue;
                    }

                    var (delivered, deliveryReason) = await HandleGroupChatAsync(session, msg, members.UserIds, cancellationToken);
                    _mq.TryEnqueue(MqEventFactory.CreateForMessage(msg, _nodeId, session.TraceId, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), session.TenantConfig.TenantSecret));

                    await SendFrameAsync(socket, new ServerFrame
                    {
                        Ack = new MessageAck
                        {
                            MsgId = msg.MsgId,
                            Status = delivered ? AckStatus.Sent : AckStatus.Failed,
                            TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                            Reason = delivered ? "" : deliveryReason
                        }
                    }, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _metrics.CounterInc("mics_messages_failed_total", 1, ("tenant", session.TenantId));
                    _logger.LogWarning(ex, "ws_message_process_failed msg={MsgId}", msg.MsgId);
                    await SendFrameAsync(socket, new ServerFrame
                    {
                        Ack = new MessageAck
                        {
                            MsgId = msg.MsgId,
                            Status = AckStatus.Failed,
                            TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                            Reason = "internal error"
                        }
                    }, cancellationToken);
                }

                continue;
            }

            // Should not happen because TryValidateMessage filters, but keep a defensive ack.
            await SendFrameAsync(socket, new ServerFrame
            {
                Ack = new MessageAck { MsgId = msg.MsgId, Status = AckStatus.Failed, TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), Reason = "invalid msg_type" }
            }, cancellationToken);
        }
	        }
	        finally
	        {
	            _traceContext.TraceId = prevTrace;
	        }
    }

    private static MessageRequest NormalizeIncomingMessage(ConnectionSession session, MessageRequest client)
    {
        var msg = client.Clone();
        msg.TenantId = session.TenantId;
        msg.UserId = session.UserId;
        msg.DeviceId = session.DeviceId;

        if (string.IsNullOrWhiteSpace(msg.MsgId))
        {
            msg.MsgId = Guid.NewGuid().ToString("N");
        }

        if (msg.TimestampMs == 0)
        {
            msg.TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        return msg;
    }

    private async Task<(bool Ok, string Reason)> HandleSingleChatAsync(
        ConnectionSession session,
        MessageRequest msg,
        IReadOnlyDictionary<string, OnlineDeviceRoute> routes,
        CancellationToken cancellationToken)
    {
        if (routes.Count == 0)
        {
            await BufferOfflineAsync(session, msg.ToUserId, msg, cancellationToken);
            return (true, "");
        }

        var ttl = TimeSpan.FromSeconds(session.TenantConfig.OfflineBufferTtlSeconds > 0 ? session.TenantConfig.OfflineBufferTtlSeconds : 300);
        var frameBytes = new ServerFrame { Delivery = new MessageDelivery { Message = msg } }.ToByteArray();

        var deliveredAny = false;
        var failed = 0;

        var byNode = routes.Values.GroupBy(r => r.NodeId, StringComparer.Ordinal);
        foreach (var group in byNode)
        {
            if (group.Key == _nodeId)
            {
                var delivered = await DeliverLocalAsync(session.TenantId, msg.ToUserId, msg, cancellationToken);
                deliveredAny |= delivered > 0;
            }
            else
            {
                var endpoint = group.First().Endpoint;
                var forwarded = await ForwardSingleAsync(group.Key, endpoint, session.TenantId, msg.ToUserId, msg, cancellationToken);
                if (forwarded)
                {
                    deliveredAny = true;
                    continue;
                }

                if (session.TenantConfig.OfflineUseHookPull)
                {
                    var evt = MqEventFactory.CreateOfflineMessage(msg, _nodeId, session.TraceId, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), session.TenantConfig.TenantSecret);
                    if (_mq.TryEnqueue(evt))
                    {
                        _metrics.CounterInc("mics_offline_notified_total", 1, ("tenant", session.TenantId));
                        deliveredAny = true;
                        continue;
                    }
                }

                var buffered = await BufferOfflineFrameBytesAsync(session, msg.ToUserId, frameBytes, ttl, cancellationToken);
                if (buffered)
                {
                    deliveredAny = true;
                }
                else
                {
                    failed++;
                }
            }
        }

        return deliveredAny ? (true, "") : (false, failed > 0 ? "delivery failed" : "no routes delivered");
    }

    private async Task<(bool Ok, string Reason)> HandleGroupChatAsync(
        ConnectionSession session,
        MessageRequest msg,
        IReadOnlyList<string> memberUserIds,
        CancellationToken cancellationToken)
    {
        _metrics.CounterInc("mics_group_messages_total", 1, ("tenant", session.TenantId));
        if (memberUserIds.Count == 0)
        {
            return (false, "group members empty");
        }

        var distinctMembers = new List<string>(memberUserIds.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var u in memberUserIds)
        {
            if (string.IsNullOrWhiteSpace(u))
            {
                continue;
            }

            if (seen.Add(u))
            {
                distinctMembers.Add(u);
            }
        }

        if (distinctMembers.Count > _groupMembersMaxUsers)
        {
            _metrics.CounterInc("mics_group_members_capped_total", distinctMembers.Count - _groupMembersMaxUsers, ("tenant", session.TenantId));
            distinctMembers.RemoveRange(_groupMembersMaxUsers, distinctMembers.Count - _groupMembersMaxUsers);
        }

        _metrics.CounterInc("mics_group_members_total", distinctMembers.Count, ("tenant", session.TenantId));
        var nodeBuckets = new Dictionary<string, (string Endpoint, HashSet<string> Users)>(StringComparer.Ordinal);
        var offlineBuffered = 0;
        var offlineNotified = 0;
        var offlineSkipped = 0;
        var deliveredAny = false;
        var ttl = TimeSpan.FromSeconds(session.TenantConfig.OfflineBufferTtlSeconds > 0 ? session.TenantConfig.OfflineBufferTtlSeconds : 300);
        var frameBytes = new ServerFrame { Delivery = new MessageDelivery { Message = msg } }.ToByteArray();

        var chunkSize = _groupRouteChunkSize;
        if (distinctMembers.Count == 0)
        {
            return (false, "group members empty");
        }

        var offset = 0;
        var currentChunk = SliceMembers(distinctMembers, offset, chunkSize);
        var currentRoutesTask = _routes.GetAllDevicesForUsersAsync(session.TenantId, currentChunk, cancellationToken);

        offset += currentChunk.Length;

        string[]? nextChunk = null;
        var hasNext = offset < distinctMembers.Count;
        ValueTask<IReadOnlyDictionary<string, IReadOnlyList<OnlineDeviceRoute>>> nextRoutesTask = default;
        if (hasNext)
        {
            nextChunk = SliceMembers(distinctMembers, offset, chunkSize);
            nextRoutesTask = _routes.GetAllDevicesForUsersAsync(session.TenantId, nextChunk, cancellationToken);
        }

        while (true)
        {
            var routesByUser = await currentRoutesTask;

            foreach (var memberUserId in currentChunk)
            {
                if (!routesByUser.TryGetValue(memberUserId, out var routes) || routes.Count == 0)
                {
                    if (session.TenantConfig.OfflineUseHookPull)
                    {
                        var evt = MqEventFactory.CreateOfflineMessageForRecipient(
                            msg,
                            memberUserId,
                            _nodeId,
                            session.TraceId,
                            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                            session.TenantConfig.TenantSecret);

                        if (_mq.TryEnqueue(evt))
                        {
                            offlineNotified++;
                            deliveredAny = true;
                            continue;
                        }
                    }

                    if (_groupOfflineBufferMaxUsers > 0 && offlineBuffered < _groupOfflineBufferMaxUsers)
                    {
                        if (await BufferOfflineFrameBytesAsync(session, memberUserId, frameBytes, ttl, cancellationToken))
                        {
                            offlineBuffered++;
                            deliveredAny = true;
                        }
                        else
                        {
                            offlineSkipped++;
                        }
                    }
                    else
                    {
                        offlineSkipped++;
                    }

                    continue;
                }

                // 一个用户可能多端分布在多个节点：将该用户加入其出现过的每个节点桶（同一节点去重由 HashSet 负责）
                foreach (var nodeRoute in routes)
                {
                    if (!nodeBuckets.TryGetValue(nodeRoute.NodeId, out var bucket))
                    {
                        bucket = (nodeRoute.Endpoint, new HashSet<string>(StringComparer.Ordinal));
                        nodeBuckets[nodeRoute.NodeId] = bucket;
                    }

                    bucket.Users.Add(memberUserId);
                }
            }

            if (!hasNext)
            {
                break;
            }

            currentChunk = nextChunk!;
            currentRoutesTask = nextRoutesTask;

            offset += currentChunk.Length;
            hasNext = offset < distinctMembers.Count;
            if (hasNext)
            {
                nextChunk = SliceMembers(distinctMembers, offset, chunkSize);
                nextRoutesTask = _routes.GetAllDevicesForUsersAsync(session.TenantId, nextChunk, cancellationToken);
            }
        }

        foreach (var (nodeId, bucket) in nodeBuckets)
        {
            if (nodeId == _nodeId)
            {
                foreach (var u in bucket.Users)
                {
                    var delivered = await DeliverLocalAsync(session.TenantId, u, msg, cancellationToken);
                    deliveredAny |= delivered > 0;
                }

                continue;
            }

            var forwarded = await ForwardBatchAsync(nodeId, bucket.Endpoint, session.TenantId, bucket.Users.ToArray(), msg, cancellationToken);
            if (forwarded)
            {
                deliveredAny = true;
                continue;
            }

            // Degrade: buffer offline for users of the failed node bucket (bounded).
            foreach (var u in bucket.Users)
            {
                if (session.TenantConfig.OfflineUseHookPull)
                {
                    var evt = MqEventFactory.CreateOfflineMessageForRecipient(
                        msg,
                        u,
                        _nodeId,
                        session.TraceId,
                        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                        session.TenantConfig.TenantSecret);

                    if (_mq.TryEnqueue(evt))
                    {
                        offlineNotified++;
                        deliveredAny = true;
                        continue;
                    }
                }

                if (_groupOfflineBufferMaxUsers > 0 && offlineBuffered >= _groupOfflineBufferMaxUsers)
                {
                    offlineSkipped++;
                    continue;
                }

                if (await BufferOfflineFrameBytesAsync(session, u, frameBytes, ttl, cancellationToken))
                {
                    offlineBuffered++;
                    deliveredAny = true;
                }
                else
                {
                    offlineSkipped++;
                }
            }
        }

        _metrics.CounterInc("mics_group_fanout_nodes_total", nodeBuckets.Count, ("tenant", session.TenantId));
        if (offlineNotified > 0)
        {
            _metrics.CounterInc("mics_group_offline_notified_total", offlineNotified, ("tenant", session.TenantId));
        }

        if (offlineBuffered > 0)
        {
            _metrics.CounterInc("mics_group_offline_buffered_total", offlineBuffered, ("tenant", session.TenantId));
        }

        if (offlineSkipped > 0)
        {
            _metrics.CounterInc("mics_group_offline_buffer_skipped_total", offlineSkipped, ("tenant", session.TenantId));
        }

        return deliveredAny ? (true, "") : (false, "delivery failed");
    }

    private static string[] SliceMembers(IReadOnlyList<string> members, int offset, int chunkSize)
    {
        var count = Math.Min(chunkSize, members.Count - offset);
        var chunk = new string[count];
        for (var i = 0; i < count; i++)
        {
            chunk[i] = members[offset + i];
        }
        return chunk;
    }

    private static bool TryValidateMessage(MessageRequest msg, out string reason)
    {
        reason = "";

        if (msg.MsgType == MessageType.SingleChat)
        {
            if (string.IsNullOrWhiteSpace(msg.ToUserId))
            {
                reason = "missing to_user_id";
                return false;
            }

            return true;
        }

        if (msg.MsgType == MessageType.GroupChat)
        {
            if (string.IsNullOrWhiteSpace(msg.GroupId))
            {
                reason = "missing group_id";
                return false;
            }

            return true;
        }

        reason = "invalid msg_type";
        return false;
    }

	    private async Task<int> DeliverLocalAsync(string tenantId, string toUserId, MessageRequest msg, CancellationToken cancellationToken)
	    {
	        var sessions = _connections.GetAllForUser(tenantId, toUserId);
	        if (sessions.Count == 0)
	        {
	            return 0;
	        }

	        var frame = new ServerFrame { Delivery = new MessageDelivery { Message = msg } };
	        using var bytes = PooledProtobufSerializer.Serialize(frame);

	        foreach (var s in sessions)
	        {
	            if (s.Socket.State != WebSocketState.Open)
	            {
	                continue;
	            }

	            await s.Socket.SendAsync(bytes.Memory, WebSocketMessageType.Binary, true, cancellationToken);
	        }

        _metrics.CounterInc("mics_deliveries_total", sessions.Count, ("tenant", tenantId), ("via", "local"));
        return sessions.Count;
    }

    private async Task<bool> ForwardSingleAsync(string targetNodeId, string endpoint, string tenantId, string toUserId, MessageRequest msg, CancellationToken cancellationToken)
    {
        const int maxRetries = 2;

        var startedAt = System.Diagnostics.Stopwatch.GetTimestamp();

        if (!_grpcBreaker.TryBegin(targetNodeId))
        {
            _metrics.CounterInc("mics_grpc_circuit_open_total", 1, ("tenant", tenantId), ("node", targetNodeId), ("via", "grpc_single"));
            return false;
        }

        var completed = false;

        try
        {

            for (var attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    var client = _nodeClients.Get(endpoint);
                    var headers = BuildClusterGrpcHeaders();
                    await client.ForwardSingleAsync(
                        new ForwardSingleRequest { TenantId = tenantId, ToUserId = toUserId, Message = msg },
                        headers: headers,
                        deadline: DateTime.UtcNow.AddMilliseconds(250),
                        cancellationToken: cancellationToken);
                    _metrics.CounterInc("mics_deliveries_total", 1, ("tenant", tenantId), ("via", "grpc_single"));
                    _grpcBreaker.OnSuccess(targetNodeId);
                    completed = true;
                    return true;
                }
                catch (RpcException ex)
                {
                    var shouldRetry = ex.StatusCode is StatusCode.Unavailable
                        or StatusCode.DeadlineExceeded
                        or StatusCode.ResourceExhausted;

                    if (shouldRetry && attempt < maxRetries)
                    {
                        _logger.LogWarning("grpc_forward_single_retry attempt={Attempt} to={ToUserId} msg={MsgId} endpoint={Endpoint} status={Status}",
                            attempt, toUserId, msg.MsgId, endpoint, ex.StatusCode);
                        await Task.Delay(50 * attempt, cancellationToken);
                        continue;
                    }

                    _grpcBreaker.OnFailure(targetNodeId, _grpcBreakerPolicy);
                    completed = true;
                    _metrics.CounterInc("mics_grpc_forward_failed_total", 1, ("tenant", tenantId), ("via", "grpc_single"), ("status", ex.StatusCode.ToString()));
                    _logger.LogWarning(ex, "grpc_forward_single_failed to={ToUserId} msg={MsgId} endpoint={Endpoint} status={Status}", toUserId, msg.MsgId, endpoint, ex.StatusCode);
                    return false;
                }
            }

            _grpcBreaker.OnFailure(targetNodeId, _grpcBreakerPolicy);
            completed = true;
            return false;
        }
        finally
        {
            var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(startedAt);
            _metrics.HistogramObserve("mics_grpc_forward_duration_ms", elapsed.TotalMilliseconds, ("tenant", tenantId), ("via", "grpc_single"));
            if (!completed)
            {
                _grpcBreaker.EndAttempt(targetNodeId);
            }
        }
    }

    private async Task<bool> ForwardBatchAsync(string targetNodeId, string endpoint, string tenantId, IReadOnlyList<string> toUserIds, MessageRequest msg, CancellationToken cancellationToken)
    {
        const int maxRetries = 2;

        var startedAt = System.Diagnostics.Stopwatch.GetTimestamp();

        if (!_grpcBreaker.TryBegin(targetNodeId))
        {
            _metrics.CounterInc("mics_grpc_circuit_open_total", toUserIds.Count, ("tenant", tenantId), ("node", targetNodeId), ("via", "grpc_batch"));
            return false;
        }

        var completed = false;

        try
        {

            for (var attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    var req = new ForwardBatchRequest { TenantId = tenantId, Message = msg };
                    req.ToUserIds.AddRange(toUserIds);

                    var client = _nodeClients.Get(endpoint);
                    var headers = BuildClusterGrpcHeaders();
                    await client.ForwardBatchAsync(req, headers: headers, deadline: DateTime.UtcNow.AddMilliseconds(250), cancellationToken: cancellationToken);
                    _metrics.CounterInc("mics_deliveries_total", toUserIds.Count, ("tenant", tenantId), ("via", "grpc_batch"));
                    _grpcBreaker.OnSuccess(targetNodeId);
                    completed = true;
                    return true;
                }
                catch (RpcException ex)
                {
                    var shouldRetry = ex.StatusCode is StatusCode.Unavailable
                        or StatusCode.DeadlineExceeded
                        or StatusCode.ResourceExhausted;

                    if (shouldRetry && attempt < maxRetries)
                    {
                        _logger.LogWarning("grpc_forward_batch_retry attempt={Attempt} users={Users} msg={MsgId} endpoint={Endpoint} status={Status}",
                            attempt, toUserIds.Count, msg.MsgId, endpoint, ex.StatusCode);
                        await Task.Delay(50 * attempt, cancellationToken);
                        continue;
                    }

                    _grpcBreaker.OnFailure(targetNodeId, _grpcBreakerPolicy);
                    completed = true;
                    _metrics.CounterInc("mics_grpc_forward_failed_total", toUserIds.Count, ("tenant", tenantId), ("via", "grpc_batch"), ("status", ex.StatusCode.ToString()));
                    _logger.LogWarning(ex, "grpc_forward_batch_failed users={Users} msg={MsgId} endpoint={Endpoint} status={Status}", toUserIds.Count, msg.MsgId, endpoint, ex.StatusCode);
                    return false;
                }
            }

            _grpcBreaker.OnFailure(targetNodeId, _grpcBreakerPolicy);
            completed = true;
            return false;
        }
        finally
        {
            var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(startedAt);
            _metrics.HistogramObserve("mics_grpc_forward_duration_ms", elapsed.TotalMilliseconds, ("tenant", tenantId), ("via", "grpc_batch"));
            if (!completed)
            {
                _grpcBreaker.EndAttempt(targetNodeId);
            }
        }
    }

    private async Task BufferOfflineAsync(ConnectionSession session, string toUserId, MessageRequest msg, CancellationToken cancellationToken)
    {
        if (session.TenantConfig.OfflineUseHookPull)
        {
            var evt = MqEventFactory.CreateOfflineMessage(msg, _nodeId, session.TraceId, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), session.TenantConfig.TenantSecret);
            if (_mq.TryEnqueue(evt))
            {
                _metrics.CounterInc("mics_offline_notified_total", 1, ("tenant", session.TenantId));
                return;
            }
        }

        var frameBytes = new ServerFrame { Delivery = new MessageDelivery { Message = msg } }.ToByteArray();
        var ttl = TimeSpan.FromSeconds(session.TenantConfig.OfflineBufferTtlSeconds > 0 ? session.TenantConfig.OfflineBufferTtlSeconds : 300);
        _ = await BufferOfflineFrameBytesAsync(session, toUserId, frameBytes, ttl, cancellationToken);
    }

    private async Task<bool> BufferOfflineFrameBytesAsync(ConnectionSession session, string toUserId, byte[] frameBytes, TimeSpan ttl, CancellationToken cancellationToken)
    {
        if (ttl <= TimeSpan.Zero)
        {
            ttl = TimeSpan.FromSeconds(300);
        }

        var homeNodeId = RendezvousHash.PickNodeId(session.TenantId, toUserId, _nodes.Current) ?? _nodeId;
        if (homeNodeId == _nodeId)
        {
            var ok = _offline.TryAdd(session.TenantId, toUserId, frameBytes, ttl);
            _metrics.CounterInc(ok ? "mics_offline_buffered_total" : "mics_offline_buffer_skipped_total", 1, ("tenant", session.TenantId), ("via", "local"));
            return ok;
        }

        var home = _nodes.Current.FirstOrDefault(n => n.NodeId == homeNodeId);
        if (home is null)
        {
            var ok = _offline.TryAdd(session.TenantId, toUserId, frameBytes, ttl);
            _metrics.CounterInc(ok ? "mics_offline_buffered_total" : "mics_offline_buffer_skipped_total", 1, ("tenant", session.TenantId), ("via", "local_fallback"));
            return ok;
        }

        try
        {
            var client = _nodeClients.Get(home.Endpoint);
            var resp = await client.BufferOfflineAsync(new BufferOfflineRequest
            {
                TenantId = session.TenantId,
                ToUserId = toUserId,
                ServerFrame = ByteString.CopyFrom(frameBytes),
                TtlSeconds = (int)Math.Clamp(ttl.TotalSeconds, 1, int.MaxValue),
            }, headers: BuildClusterGrpcHeaders(), cancellationToken: cancellationToken);

            if (resp.Ok)
            {
                _metrics.CounterInc("mics_offline_buffered_total", 1, ("tenant", session.TenantId), ("via", "grpc"));
                return true;
            }

            _metrics.CounterInc("mics_offline_buffer_skipped_total", 1, ("tenant", session.TenantId), ("via", "grpc"));
            return false;
        }
        catch (RpcException ex)
        {
            _metrics.CounterInc("mics_grpc_offline_buffer_failed_total", 1, ("tenant", session.TenantId), ("status", ex.StatusCode.ToString()));
            _logger.LogWarning(ex, "grpc_offline_buffer_failed to={ToUserId} endpoint={Endpoint} status={Status}", toUserId, home.Endpoint, ex.StatusCode);
            var ok = _offline.TryAdd(session.TenantId, toUserId, frameBytes, ttl);
            _metrics.CounterInc(ok ? "mics_offline_buffered_total" : "mics_offline_buffer_skipped_total", 1, ("tenant", session.TenantId), ("via", "local_fallback"));
            return ok;
        }
    }

    private async Task DrainOfflineAsync(ConnectionSession session, CancellationToken cancellationToken)
    {
        if (session.TenantConfig.OfflineUseHookPull)
        {
            await DrainOfflineFromHookAsync(session, cancellationToken);
        }

        var homeNodeId = RendezvousHash.PickNodeId(session.TenantId, session.UserId, _nodes.Current) ?? _nodeId;
        IReadOnlyList<byte[]> frames;

        if (homeNodeId == _nodeId)
        {
            frames = _offline.Drain(session.TenantId, session.UserId);
        }
        else
        {
            var home = _nodes.Current.FirstOrDefault(n => n.NodeId == homeNodeId);
            if (home is null)
            {
                return;
            }

            try
            {
                var client = _nodeClients.Get(home.Endpoint);
                var resp = await client.DrainOfflineAsync(
                    new DrainOfflineRequest { TenantId = session.TenantId, UserId = session.UserId },
                    headers: BuildClusterGrpcHeaders(),
                    cancellationToken: cancellationToken);
                frames = resp.ServerFrames.Select(b => b.ToByteArray()).ToArray();
            }
            catch (RpcException ex)
            {
                _metrics.CounterInc("mics_grpc_offline_drain_failed_total", 1, ("tenant", session.TenantId), ("status", ex.StatusCode.ToString()));
                _logger.LogWarning(ex, "grpc_offline_drain_failed user={UserId} endpoint={Endpoint} status={Status}", session.UserId, home.Endpoint, ex.StatusCode);
                return;
            }
        }

        foreach (var frameBytes in frames)
        {
            if (session.Socket.State != WebSocketState.Open)
            {
                return;
            }

            await session.Socket.SendAsync(frameBytes, WebSocketMessageType.Binary, true, cancellationToken);
        }

        if (frames.Count > 0)
        {
            _metrics.CounterInc("mics_offline_drained_total", frames.Count, ("tenant", session.TenantId), ("node", _nodeId));
        }
    }

    private async Task DrainOfflineFromHookAsync(ConnectionSession session, CancellationToken cancellationToken)
    {
        try
        {
            var cursor = "";
            var drained = 0;
            const int maxMessages = 100;
            const int maxPages = 10;
            const int maxTotal = 1000;

            for (var page = 0; page < maxPages && drained < maxTotal; page++)
            {
                var result = await _hook.GetOfflineMessagesAsync(
                    session.TenantConfig,
                    session.TenantId,
                    session.UserId,
                    session.DeviceId,
                    maxMessages,
                    cursor,
                    cancellationToken);

                _metrics.CounterInc(
                    "mics_hook_get_offline_messages_total",
                    1,
                    ("tenant", session.TenantId),
                    ("result", result.Degraded ? "degraded" : result.Ok ? "ok" : "fail"));

                if (!result.Ok)
                {
                    if (!result.Degraded)
                    {
                        _metrics.CounterInc("mics_offline_drain_failed_total", 1, ("tenant", session.TenantId), ("reason", "hook_fail"));
                    }
                    return;
                }

                if (result.Messages.Count == 0)
                {
                    return;
                }

                foreach (var msg in result.Messages)
                {
                    if (session.Socket.State != WebSocketState.Open)
                    {
                        return;
                    }

                    await SendFrameAsync(
                        session.Socket,
                        new ServerFrame { Delivery = new MessageDelivery { Message = msg } },
                        cancellationToken);

                    drained++;
                    if (drained >= maxTotal)
                    {
                        _logger.LogWarning("offline_drain_hook_limit_reached user={UserId} count={Count}", session.UserId, drained);
                        break;
                    }
                }

                if (!result.HasMore)
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(result.NextCursor))
                {
                    _logger.LogWarning("offline_drain_hook_missing_cursor user={UserId}", session.UserId);
                    break;
                }

                cursor = result.NextCursor;
            }

            if (drained > 0)
            {
                _metrics.CounterInc("mics_offline_drained_from_hook_total", drained, ("tenant", session.TenantId), ("node", _nodeId));
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _metrics.CounterInc("mics_offline_drain_failed_total", 1, ("tenant", session.TenantId), ("reason", "exception"));
            _logger.LogWarning(ex, "offline_drain_hook_failed user={UserId}", session.UserId);
        }
    }

    private void UpdateConnectionGauges(string tenantId, long delta)
    {
        var current = Interlocked.Add(ref _activeConnections, delta);
        _metrics.GaugeSet("mics_ws_connections", current, ("node", _nodeId));
        _metrics.CounterInc("mics_ws_connections_events_total", 1, ("tenant", tenantId), ("node", _nodeId), ("delta", delta > 0 ? "inc" : "dec"));
    }

    private static async Task SendFrameAsync(WebSocket socket, ServerFrame frame, CancellationToken cancellationToken)
    {
        await WebSocketMessageIO.SendMessageAsync(socket, frame, cancellationToken);
    }

    private static async Task CloseAsync(WebSocket socket, int closeCode, string? reason, CancellationToken cancellationToken)
    {
        try
        {
            await socket.CloseAsync((WebSocketCloseStatus)closeCode, reason, cancellationToken);
        }
        catch
        {
        }
    }

    private static Metadata? BuildClusterGrpcHeaders()
    {
        var token = Environment.GetEnvironmentVariable("CLUSTER_GRPC_TOKEN");
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        return new Metadata { { "x-mics-node-token", token } };
    }
}
