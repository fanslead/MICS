using System.Globalization;
using System.Net.WebSockets;
using System.Collections.Concurrent;
using Google.Protobuf;
using Mics.Contracts.Message.V1;

namespace Mics.Client;

public sealed class MicsClient : IAsyncDisposable
{
    private readonly MicsClientOptions _options;
    private readonly Func<IMicsWebSocket> _webSocketFactory;
    private readonly object _gate = new();
    private readonly CancellationTokenSource _disposeCts = new();

    private IMicsWebSocket? _ws;
    private CancellationTokenSource? _lifetimeCts;
    private Task? _receiveTask;
    private Task? _heartbeatTask;
    private Task? _reconnectTask;
    private MicsClientState _state;
    private MicsSession? _session;
    private long _nextMsgId;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<MessageAck>> _pendingAcks = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private ConnectParams? _connectParams;
    private TaskCompletionSource<bool> _connectedSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public event Action<MicsClientState>? StateChanged;
    public event Action<MicsSession>? Connected;
    public event Action<MessageDelivery>? DeliveryReceived;
    public event Action<MessageAck>? AckReceived;
    public event Action<ServerError>? ServerErrorReceived;

    public MicsClientState State => _state;

    public MicsClient(MicsClientOptions? options = null)
        : this(options, () => new ClientWebSocketAdapter())
    {
    }

    internal MicsClient(MicsClientOptions? options, Func<IMicsWebSocket> webSocketFactory)
    {
        _options = options ?? MicsClientOptions.Default;
        _webSocketFactory = webSocketFactory;
        _state = MicsClientState.Disconnected;
    }

    public async ValueTask ConnectAsync(Uri gatewayWsUrl, string tenantId, string token, string deviceId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(gatewayWsUrl);
        tenantId ??= "";
        token ??= "";
        deviceId ??= "";

        lock (_gate)
        {
            if (_state is not MicsClientState.Disconnected)
            {
                throw new InvalidOperationException("Client is not disconnected.");
            }

            SetStateLocked(MicsClientState.Connecting);
            _connectParams = new ConnectParams(gatewayWsUrl, tenantId, token, deviceId);
        }

        var ws = _webSocketFactory();
        var connectUri = WsUriBuilder.Build(gatewayWsUrl, tenantId, token, deviceId);

        var lifetimeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposeCts.Token);
        lifetimeCts.CancelAfter(_options.ConnectTimeout);

        var connectAck = new TaskCompletionSource<MicsSession>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await ws.ConnectAsync(connectUri, lifetimeCts.Token);

            lock (_gate)
            {
                _ws = ws;
                _lifetimeCts = lifetimeCts;
                _receiveTask = ReceiveLoopAsync(ws, connectAck, tenantId, deviceId, lifetimeCts.Token);
            }

            var session = await connectAck.Task.WaitAsync(lifetimeCts.Token);
            Connected?.Invoke(session);
            lock (_gate)
            {
                _session = session;
            }

            lock (_gate)
            {
                SetStateLocked(MicsClientState.Connected);
                _heartbeatTask = StartHeartbeatLoopLocked(ws, lifetimeCts.Token);
            }
        }
        catch
        {
            try { await ws.DisposeAsync(); } catch { }
            lifetimeCts.Dispose();
            lock (_gate)
            {
                SetStateLocked(MicsClientState.Disconnected);
                _ws = null;
                _receiveTask = null;
                _lifetimeCts = null;
                _session = null;
                _heartbeatTask = null;
                _reconnectTask = null;
            }
            throw;
        }
    }

    public async ValueTask<MessageAck> SendSingleChatAsync(string toUserId, ReadOnlyMemory<byte> msgBody, string? msgId = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(toUserId))
        {
            throw new ArgumentException("toUserId is required.", nameof(toUserId));
        }

        var session = (await GetConnectedOrWaitAsync(cancellationToken)).Session;

        var effectiveMsgId = string.IsNullOrWhiteSpace(msgId)
            ? Interlocked.Increment(ref _nextMsgId).ToString(CultureInfo.InvariantCulture)
            : msgId;

        var message = new MessageRequest
        {
            TenantId = session.TenantId,
            UserId = session.UserId,
            DeviceId = session.DeviceId,
            MsgId = effectiveMsgId,
            MsgType = MessageType.SingleChat,
            ToUserId = toUserId,
            GroupId = "",
            MsgBody = ByteString.Empty,
            TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };

        message.MsgBody = PrepareOutboundBody(msgBody);

        return await SendWithRetryAsync(effectiveMsgId, message, cancellationToken);
    }

    public async ValueTask<MessageAck> SendGroupChatAsync(string groupId, ReadOnlyMemory<byte> msgBody, string? msgId = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(groupId))
        {
            throw new ArgumentException("groupId is required.", nameof(groupId));
        }

        var session = (await GetConnectedOrWaitAsync(cancellationToken)).Session;

        var effectiveMsgId = string.IsNullOrWhiteSpace(msgId)
            ? Interlocked.Increment(ref _nextMsgId).ToString(CultureInfo.InvariantCulture)
            : msgId;

        var message = new MessageRequest
        {
            TenantId = session.TenantId,
            UserId = session.UserId,
            DeviceId = session.DeviceId,
            MsgId = effectiveMsgId,
            MsgType = MessageType.GroupChat,
            ToUserId = "",
            GroupId = groupId,
            MsgBody = ByteString.Empty,
            TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };

        message.MsgBody = PrepareOutboundBody(msgBody);

        return await SendWithRetryAsync(effectiveMsgId, message, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        IMicsWebSocket? ws;
        CancellationTokenSource? cts;
        Task? receiveTask;
        Task? heartbeatTask;
        Task? reconnectTask;

        lock (_gate)
        {
            if (_state == MicsClientState.Disposing)
            {
                return;
            }

            SetStateLocked(MicsClientState.Disposing);
            ws = _ws;
            cts = _lifetimeCts;
            receiveTask = _receiveTask;
            heartbeatTask = _heartbeatTask;
            reconnectTask = _reconnectTask;
            _ws = null;
            _lifetimeCts = null;
            _receiveTask = null;
            _heartbeatTask = null;
            _reconnectTask = null;
        }

        try { _disposeCts.Cancel(); } catch { }
        try { cts?.Cancel(); } catch { }

        if (ws is not null)
        {
            try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "dispose", CancellationToken.None); } catch { }
            try { await ws.DisposeAsync(); } catch { }
        }

        if (receiveTask is not null)
        {
            try { await receiveTask; } catch { }
        }

        if (heartbeatTask is not null)
        {
            try { await heartbeatTask; } catch { }
        }

        if (reconnectTask is not null)
        {
            try { await reconnectTask; } catch { }
        }

        cts?.Dispose();
        _disposeCts.Dispose();

        lock (_gate)
        {
            SetStateLocked(MicsClientState.Disconnected);
            _session = null;
        }
    }

    private void SetStateLocked(MicsClientState state)
    {
        if (_state == state)
        {
            return;
        }

        _state = state;

        if (state == MicsClientState.Connected)
        {
            _connectedSignal.TrySetResult(true);
        }
        else if (_connectedSignal.Task.IsCompleted)
        {
            _connectedSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        StateChanged?.Invoke(state);
    }

    private async Task ReceiveLoopAsync(
        IMicsWebSocket ws,
        TaskCompletionSource<MicsSession> connectAck,
        string tenantId,
        string deviceId,
        CancellationToken cancellationToken)
    {
        const int bufferBytes = 8 * 1024;
        var buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(bufferBytes);
        byte[]? acc = null;
        var accLen = 0;

        try
        {
            while (!cancellationToken.IsCancellationRequested && ws.State == WebSocketState.Open)
            {
                var result = await ws.ReceiveAsync(buffer, cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }

                if (result.Count == 0 && result.EndOfMessage)
                {
                    continue;
                }

                if (acc is null && result.EndOfMessage)
                {
                    TryHandleFrame(buffer, result.Count, connectAck, tenantId, deviceId);
                    continue;
                }

                acc = EnsureCapacity(acc, accLen, accLen + result.Count);
                buffer.AsSpan(0, result.Count).CopyTo(acc.AsSpan(accLen));
                accLen += result.Count;

                if (result.EndOfMessage)
                {
                    TryHandleFrame(acc, accLen, connectAck, tenantId, deviceId);
                    accLen = 0;
                }
            }

            if (!connectAck.Task.IsCompleted)
            {
                connectAck.TrySetException(new InvalidOperationException("Connection closed before connect ack."));
            }

            if (!cancellationToken.IsCancellationRequested)
            {
                TryStartReconnect();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            connectAck.TrySetException(ex);
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
            if (acc is not null)
            {
                System.Buffers.ArrayPool<byte>.Shared.Return(acc);
            }
        }
    }

    private void TryHandleFrame(byte[] payload, int length, TaskCompletionSource<MicsSession> connectAck, string tenantId, string deviceId)
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
                if (frame.ConnectAck.Code != 1000)
                {
                    connectAck.TrySetException(new InvalidOperationException("Connect failed: " + frame.ConnectAck.Code.ToString(CultureInfo.InvariantCulture)));
                    return;
                }

                connectAck.TrySetResult(new MicsSession(
                    TenantId: string.IsNullOrWhiteSpace(frame.ConnectAck.TenantId) ? tenantId : frame.ConnectAck.TenantId,
                    UserId: frame.ConnectAck.UserId ?? "",
                    DeviceId: string.IsNullOrWhiteSpace(frame.ConnectAck.DeviceId) ? deviceId : frame.ConnectAck.DeviceId,
                    NodeId: frame.ConnectAck.NodeId ?? "",
                    TraceId: frame.ConnectAck.TraceId ?? ""));
                return;
            case ServerFrame.PayloadOneofCase.Ack:
                var ack = frame.Ack;
                if (!string.IsNullOrWhiteSpace(ack.MsgId) && _pendingAcks.TryGetValue(ack.MsgId, out var tcs))
                {
                    tcs.TrySetResult(ack);
                }
                AckReceived?.Invoke(ack);
                return;
            case ServerFrame.PayloadOneofCase.Delivery:
                var delivery = frame.Delivery;
                var crypto = _options.MessageCrypto;
                var msg = delivery.Message;
                if (crypto is not null && msg is not null && msg.MsgBody.Length > 0)
                {
                    try
                    {
                        var decrypted = crypto.Decrypt(msg.MsgBody.Span);
                        var clone = msg.Clone();
                        clone.MsgBody = decrypted.Length == 0 ? ByteString.Empty : ByteString.CopyFrom(decrypted);
                        delivery = new MessageDelivery { Message = clone };
                    }
                    catch
                    {
                    }
                }

                DeliveryReceived?.Invoke(delivery);
                return;
            case ServerFrame.PayloadOneofCase.Error:
                ServerErrorReceived?.Invoke(frame.Error);
                return;
            default:
                return;
        }
    }

    private static byte[] EnsureCapacity(byte[]? acc, int accLen, int size)
    {
        if (acc is not null && acc.Length >= size)
        {
            return acc;
        }

        var next = System.Buffers.ArrayPool<byte>.Shared.Rent(size);
        if (acc is not null)
        {
            if (accLen > 0)
            {
                acc.AsSpan(0, accLen).CopyTo(next);
            }
            System.Buffers.ArrayPool<byte>.Shared.Return(acc);
        }

        return next;
    }

    private (IMicsWebSocket Ws, MicsSession Session) GetConnected()
    {
        lock (_gate)
        {
            if (_state != MicsClientState.Connected || _ws is null || _session is null)
            {
                throw new InvalidOperationException("Client is not connected.");
            }

            return (_ws, _session);
        }
    }

    private async ValueTask<(IMicsWebSocket Ws, MicsSession Session)> GetConnectedOrWaitAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            Task wait;
            lock (_gate)
            {
                if (_state == MicsClientState.Connected && _ws is not null && _session is not null)
                {
                    return (_ws, _session);
                }

                if (_state is MicsClientState.Disposing or MicsClientState.Disconnected)
                {
                    throw new InvalidOperationException("Client is not connected.");
                }

                if (!_options.AutoReconnect)
                {
                    throw new InvalidOperationException("Client is not connected.");
                }

                wait = _connectedSignal.Task;
            }

            await wait.WaitAsync(cancellationToken);
        }
    }

    private Task? StartHeartbeatLoopLocked(IMicsWebSocket ws, CancellationToken cancellationToken)
    {
        if (_options.HeartbeatInterval <= TimeSpan.Zero || _options.HeartbeatInterval == Timeout.InfiniteTimeSpan)
        {
            return null;
        }

        return Task.Run(() => HeartbeatLoopAsync(ws, _options.HeartbeatInterval, cancellationToken), cancellationToken);
    }

    private async Task HeartbeatLoopAsync(IMicsWebSocket ws, TimeSpan interval, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(interval);
        var ping = new HeartbeatPing();
        var frame = new ClientFrame { HeartbeatPing = ping };

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                ping.TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var bytes = frame.ToByteArray();
                await SendBinaryAsync(ws, bytes, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async ValueTask SendBinaryAsync(IMicsWebSocket ws, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            if (ws.State != WebSocketState.Open)
            {
                throw new WebSocketException(WebSocketError.InvalidState);
            }

            await ws.SendAsync(payload, WebSocketMessageType.Binary, WebSocketMessageFlags.EndOfMessage, cancellationToken);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async ValueTask<MessageAck> SendWithRetryAsync(string msgId, MessageRequest message, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<MessageAck>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pendingAcks.TryAdd(msgId, tcs))
        {
            throw new InvalidOperationException("Duplicate msgId in flight: " + msgId);
        }

        try
        {
            var maxSendAttempts = Math.Max(1, _options.MaxSendAttempts);
            var sentAttempts = 0;

            while (sentAttempts < maxSendAttempts)
            {
                message.TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var frame = new ClientFrame { Message = message };
                var bytes = frame.ToByteArray();

                try
                {
                    var ws = (await GetConnectedOrWaitAsync(cancellationToken)).Ws;
                    await SendBinaryAsync(ws, bytes, cancellationToken);
                    sentAttempts++;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    TryStartReconnect();
                    continue;
                }

                var completed = await Task.WhenAny(tcs.Task, Task.Delay(_options.AckTimeout, cancellationToken));
                if (completed == tcs.Task)
                {
                    return await tcs.Task;
                }
            }

            return new MessageAck
            {
                MsgId = msgId,
                Status = AckStatus.Failed,
                TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Reason = "ack timeout"
            };
        }
        finally
        {
            _pendingAcks.TryRemove(msgId, out _);
        }
    }

    private ByteString PrepareOutboundBody(ReadOnlyMemory<byte> plaintext)
    {
        if (plaintext.Length == 0)
        {
            return ByteString.Empty;
        }

        var crypto = _options.MessageCrypto;
        if (crypto is null)
        {
            return ByteString.CopyFrom(plaintext.Span);
        }

        var ciphertext = crypto.Encrypt(plaintext.Span);
        return ciphertext.Length == 0 ? ByteString.Empty : ByteString.CopyFrom(ciphertext);
    }

    private void TryStartReconnect()
    {
        if (!_options.AutoReconnect)
        {
            return;
        }

        ConnectParams? p;
        IMicsWebSocket? oldWs;
        CancellationTokenSource? oldCts;

        lock (_gate)
        {
            if (_state is MicsClientState.Disposing or MicsClientState.Disconnected or MicsClientState.Connecting)
            {
                return;
            }

            if (_reconnectTask is not null)
            {
                return;
            }

            p = _connectParams;
            if (p is null)
            {
                return;
            }

            oldWs = _ws;
            oldCts = _lifetimeCts;
            _ws = null;
            _lifetimeCts = null;
            _receiveTask = null;
            _heartbeatTask = null;
            _session = null;

            SetStateLocked(MicsClientState.Reconnecting);
            _reconnectTask = Task.Run(() => ReconnectLoopAsync(p, _disposeCts.Token), CancellationToken.None);
        }

        try { oldCts?.Cancel(); } catch { }
        oldCts?.Dispose();
        if (oldWs is not null)
        {
            _ = oldWs.DisposeAsync();
        }
    }

    private async Task ReconnectLoopAsync(ConnectParams p, CancellationToken cancellationToken)
    {
        var delay = _options.ReconnectMinDelay;
        if (delay <= TimeSpan.Zero) delay = TimeSpan.FromMilliseconds(50);
        var max = _options.ReconnectMaxDelay <= TimeSpan.Zero ? TimeSpan.FromSeconds(5) : _options.ReconnectMaxDelay;
        if (delay > max) delay = max;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await ReconnectOnceAsync(p, cancellationToken);
                return;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                await Task.Delay(delay, cancellationToken);
                var nextMs = Math.Min(max.TotalMilliseconds, delay.TotalMilliseconds * 2);
                delay = TimeSpan.FromMilliseconds(nextMs);
            }
        }
    }

    private async Task ReconnectOnceAsync(ConnectParams p, CancellationToken cancellationToken)
    {
        var ws = _webSocketFactory();
        var connectUri = WsUriBuilder.Build(p.GatewayWsUrl, p.TenantId, p.Token, p.DeviceId);

        var lifetimeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lifetimeCts.CancelAfter(_options.ConnectTimeout);

        var connectAck = new TaskCompletionSource<MicsSession>(TaskCreationOptions.RunContinuationsAsynchronously);
        await ws.ConnectAsync(connectUri, lifetimeCts.Token);

        lock (_gate)
        {
            _ws = ws;
            _lifetimeCts = lifetimeCts;
            _receiveTask = ReceiveLoopAsync(ws, connectAck, p.TenantId, p.DeviceId, lifetimeCts.Token);
        }

        var session = await connectAck.Task.WaitAsync(lifetimeCts.Token);
        Connected?.Invoke(session);

        lock (_gate)
        {
            _session = session;
            SetStateLocked(MicsClientState.Connected);
            _heartbeatTask = StartHeartbeatLoopLocked(ws, lifetimeCts.Token);
            _reconnectTask = null;
        }
    }

    private sealed record ConnectParams(Uri GatewayWsUrl, string TenantId, string Token, string DeviceId);
}
