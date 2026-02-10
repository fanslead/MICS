using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.Net.WebSockets;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Mics.Contracts.Message.V1;

namespace Mics.LoadTester;

internal sealed class ConnectionWorker
{
    private const int DefaultBufferBytes = 8 * 1024;
    private const int AckRingSize = 4096;

    private readonly int _index;
    private readonly LoadTestOptions _options;
    private readonly LoadStats _stats;
    private readonly string _userId;
    private readonly string _token;
    private readonly string _deviceId;
    private readonly string _toUserId;
    private readonly ByteString _payload;

    private readonly long[] _sentIdRing = new long[AckRingSize];
    private readonly long[] _sentTicksRing = new long[AckRingSize];
    private long _nextMsgId;

    public ConnectionWorker(int index, LoadTestOptions options, LoadStats stats)
    {
        _index = index;
        _options = options;
        _stats = stats;

        _userId = "u" + (index + 1).ToString(CultureInfo.InvariantCulture);
        _token = options.TokenPrefix + _userId;
        _deviceId = options.DevicePrefix + _userId;

        var to = (index + 2);
        if (to > options.Connections)
        {
            to = 1;
        }
        _toUserId = "u" + to.ToString(CultureInfo.InvariantCulture);

        if (options.PayloadBytes <= 0)
        {
            _payload = ByteString.Empty;
        }
        else
        {
            var bytes = new byte[options.PayloadBytes];
            bytes.AsSpan().Fill((byte)(index & 0xFF));
            _payload = ByteString.CopyFrom(bytes);
        }
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _stats.ConnectionsAttempted);

        using var ws = new ClientWebSocket();
        ws.Options.KeepAliveInterval = TimeSpan.Zero;
        ws.Options.SetBuffer(DefaultBufferBytes, DefaultBufferBytes);

        var uri = WsUriBuilder.Build(_options.BaseUrl, _options.TenantId, _token, _deviceId);
        var connectStarted = Stopwatch.GetTimestamp();

        try
        {
            await ws.ConnectAsync(uri, cancellationToken);
        }
        catch
        {
            Interlocked.Increment(ref _stats.ConnectionsFailed);
            return;
        }

        Interlocked.Increment(ref _stats.ConnectionsOpen);

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var receiveReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var receiveTask = ReceiveLoopAsync(ws, receiveReady, connectStarted, linked.Token);

        if (!await WaitConnectAckAsync(receiveReady.Task, cancellationToken))
        {
            Interlocked.Increment(ref _stats.ConnectionsFailed);
            linked.Cancel();
            try { await receiveTask; } catch { }
            return;
        }

        Task? sendTask = null;
        var qps = _options.SendQpsPerConnection;
        if (_options.Mode != LoadTestMode.ConnectOnly && qps <= 0)
        {
            qps = 1;
        }

        if (_options.Mode == LoadTestMode.Heartbeat && qps > 0)
        {
            sendTask = SendHeartbeatLoopAsync(ws, qps, linked.Token);
        }
        else if ((_options.Mode == LoadTestMode.SingleChat || _options.Mode == LoadTestMode.GroupChat) && qps > 0)
        {
            sendTask = SendMessageLoopAsync(ws, qps, linked.Token);
        }

        try
        {
            if (sendTask is not null)
            {
                await Task.WhenAll(receiveTask, sendTask);
            }
            else
            {
                await receiveTask;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
        }
        finally
        {
            linked.Cancel();
            Interlocked.Decrement(ref _stats.ConnectionsOpen);
            Interlocked.Increment(ref _stats.ConnectionsClosed);
            try
            {
                if (ws.State is WebSocketState.Open or WebSocketState.CloseReceived)
                {
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
                }
            }
            catch
            {
            }
        }
    }

    private static async Task<bool> WaitConnectAckAsync(Task<bool> ackTask, CancellationToken cancellationToken)
    {
        try
        {
            var completed = await Task.WhenAny(ackTask, Task.Delay(TimeSpan.FromSeconds(5), cancellationToken));
            return completed == ackTask && await ackTask;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private async Task ReceiveLoopAsync(ClientWebSocket ws, TaskCompletionSource<bool> connectAck, long connectStartedTicks, CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(DefaultBufferBytes);
        byte[]? acc = null;
        var accLen = 0;

        try
        {
            while (!cancellationToken.IsCancellationRequested && ws.State == WebSocketState.Open)
            {
                var result = await ws.ReceiveAsync(buffer, cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    RecordCloseCode(ws);
                    connectAck.TrySetResult(false);
                    break;
                }

                if (result.Count == 0 && result.EndOfMessage)
                {
                    continue;
                }

                if (acc is null && result.EndOfMessage)
                {
                    HandleFrame(buffer, result.Count, connectAck, connectStartedTicks);
                    continue;
                }

                acc = EnsureCapacity(acc, accLen + result.Count);
                Buffer.BlockCopy(buffer, 0, acc, accLen, result.Count);
                accLen += result.Count;

                if (result.EndOfMessage)
                {
                    HandleFrame(acc, accLen, connectAck, connectStartedTicks);
                    accLen = 0;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
        }
        finally
        {
            if (acc is not null)
            {
                ArrayPool<byte>.Shared.Return(acc);
            }
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private void RecordCloseCode(ClientWebSocket ws)
    {
        try
        {
            var code = (int)(ws.CloseStatus ?? 0);
            if (code != 0)
            {
                _stats.CloseCodeCounts.AddOrUpdate(code, 1, static (_, v) => v + 1);
            }
        }
        catch
        {
        }
    }

    private static byte[] EnsureCapacity(byte[]? existing, int needed)
    {
        if (existing is null)
        {
            return ArrayPool<byte>.Shared.Rent(Math.Max(needed, DefaultBufferBytes));
        }

        if (existing.Length >= needed)
        {
            return existing;
        }

        var next = ArrayPool<byte>.Shared.Rent(Math.Max(needed, existing.Length * 2));
        Buffer.BlockCopy(existing, 0, next, 0, existing.Length);
        ArrayPool<byte>.Shared.Return(existing);
        return next;
    }

    private void HandleFrame(byte[] payload, int length, TaskCompletionSource<bool> connectAck, long connectStartedTicks)
    {
        ServerFrame frame;
        try
        {
            var cis = new CodedInputStream(payload, 0, length);
            frame = ServerFrame.Parser.ParseFrom(cis);
        }
        catch
        {
            return;
        }

        switch (frame.PayloadCase)
        {
            case ServerFrame.PayloadOneofCase.ConnectAck:
                if (frame.ConnectAck.Code == 1000)
                {
                    var elapsedMs = ElapsedMs(connectStartedTicks);
                    _stats.ConnectLatencyMs.RecordMs(elapsedMs);
                    Interlocked.Increment(ref _stats.ConnectionsOk);
                    connectAck.TrySetResult(true);
                }
                else
                {
                    Interlocked.Increment(ref _stats.ConnectionsFailed);
                    connectAck.TrySetResult(false);
                }
                break;
            case ServerFrame.PayloadOneofCase.HeartbeatPong:
                Interlocked.Increment(ref _stats.HeartbeatPong);
                break;
            case ServerFrame.PayloadOneofCase.Ack:
                Interlocked.Increment(ref _stats.MessageAcks);
                if (frame.Ack.Status == AckStatus.Failed)
                {
                    Interlocked.Increment(ref _stats.MessageAckFailed);
                }
                RecordAckLatency(frame.Ack.MsgId);
                break;
            case ServerFrame.PayloadOneofCase.Delivery:
                Interlocked.Increment(ref _stats.MessageDeliveries);
                var ts = frame.Delivery.Message?.TimestampMs ?? 0;
                if (ts > 0)
                {
                    var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    var ms = (int)Math.Clamp(now - ts, 0, 10_000);
                    _stats.DeliveryLatencyMs.RecordMs(ms);
                }
                break;
            case ServerFrame.PayloadOneofCase.Error:
                Interlocked.Increment(ref _stats.ServerErrors);
                break;
        }
    }

    private void RecordAckLatency(string msgId)
    {
        if (!long.TryParse(msgId, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
        {
            return;
        }

        var idx = (int)(id & (AckRingSize - 1));
        var stored = Volatile.Read(ref _sentIdRing[idx]);
        if (stored != id)
        {
            return;
        }

        var ticks = Volatile.Read(ref _sentTicksRing[idx]);
        if (ticks == 0)
        {
            return;
        }

        _stats.AckLatencyMs.RecordMs(ElapsedMs(ticks));
    }

    private static int ElapsedMs(long startedTicks)
    {
        var delta = Stopwatch.GetTimestamp() - startedTicks;
        if (delta <= 0)
        {
            return 0;
        }

        return (int)Math.Clamp((delta * 1000) / Stopwatch.Frequency, 0, 10_000);
    }

    private async Task SendHeartbeatLoopAsync(ClientWebSocket ws, double qps, CancellationToken cancellationToken)
    {
        var interval = qps <= 0 ? TimeSpan.FromSeconds(1) : TimeSpan.FromSeconds(1d / qps);
        var timer = new PeriodicTimer(interval);

        var ping = new HeartbeatPing();
        var frame = new ClientFrame { HeartbeatPing = ping };

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                ping.TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                using var bytes = PooledProtobufSerializer.Serialize(frame);
                await ws.SendAsync(bytes.Memory, WebSocketMessageType.Binary, true, cancellationToken);
                Interlocked.Increment(ref _stats.HeartbeatSent);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
        }
        finally
        {
            timer.Dispose();
        }
    }

    private async Task SendMessageLoopAsync(ClientWebSocket ws, double qps, CancellationToken cancellationToken)
    {
        var interval = qps <= 0 ? TimeSpan.FromSeconds(1) : TimeSpan.FromSeconds(1d / qps);
        var timer = new PeriodicTimer(interval);

        var msgType = _options.Mode == LoadTestMode.GroupChat ? MessageType.GroupChat : MessageType.SingleChat;
        var request = new MessageRequest
        {
            TenantId = _options.TenantId,
            UserId = _userId,
            DeviceId = _deviceId,
            MsgType = msgType,
            ToUserId = msgType == MessageType.SingleChat ? _toUserId : "",
            GroupId = msgType == MessageType.GroupChat ? (_options.GroupId ?? "") : "",
            MsgBody = _payload,
        };

        var frame = new ClientFrame { Message = request };

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                var id = Interlocked.Increment(ref _nextMsgId);
                request.MsgId = id.ToString(CultureInfo.InvariantCulture);
                request.TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                var idx = (int)(id & (AckRingSize - 1));
                Volatile.Write(ref _sentIdRing[idx], id);
                Volatile.Write(ref _sentTicksRing[idx], Stopwatch.GetTimestamp());

                try
                {
                    using var bytes = PooledProtobufSerializer.Serialize(frame);
                    await ws.SendAsync(bytes.Memory, WebSocketMessageType.Binary, true, cancellationToken);
                    Interlocked.Increment(ref _stats.MessagesSent);
                }
                catch
                {
                    Interlocked.Increment(ref _stats.MessageSendFailed);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            timer.Dispose();
        }
    }
}
