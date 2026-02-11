using System.Net.WebSockets;
using System.Threading.Channels;
using Google.Protobuf;
using Mics.Client;
using Mics.Contracts.Message.V1;

namespace Mics.Tests;

public sealed class MicsClientSdkTests
{
    [Fact]
    public async Task ConnectAsync_RaisesConnected_WithSessionFromConnectAck()
    {
        await using var ws = new ScriptedWebSocket();
        ws.EnqueueReceive(new ServerFrame
        {
            ConnectAck = new ConnectAck
            {
                Code = 1000,
                TenantId = "t1",
                UserId = "u1",
                DeviceId = "d1",
                NodeId = "n1",
                TraceId = "tr1",
            }
        }.ToByteArray(), endOfMessage: true);

        var options = MicsClientOptions.Default with { AutoReconnect = false, HeartbeatInterval = Timeout.InfiniteTimeSpan };
        var client = new MicsClient(options, () => ws);

        MicsSession? session = null;
        client.Connected += s => session = s;

        await client.ConnectAsync(new Uri("ws://localhost/ws"), tenantId: "t1", token: "valid:u1", deviceId: "d1", CancellationToken.None);

        Assert.NotNull(session);
        Assert.Equal("t1", session!.TenantId);
        Assert.Equal("u1", session.UserId);
        Assert.Equal("d1", session.DeviceId);
        Assert.Equal("n1", session.NodeId);
        Assert.Equal("tr1", session.TraceId);
    }

    [Fact]
    public async Task SendSingleChatAsync_SendsFrame_AndReturnsAck()
    {
        await using var ws = new ScriptedWebSocket();
        ws.EnqueueReceive(new ServerFrame
        {
            ConnectAck = new ConnectAck
            {
                Code = 1000,
                TenantId = "t1",
                UserId = "u1",
                DeviceId = "d1",
                NodeId = "n1",
                TraceId = "tr1",
            }
        }.ToByteArray(), endOfMessage: true);

        var options = MicsClientOptions.Default with { AutoReconnect = false, HeartbeatInterval = Timeout.InfiniteTimeSpan, AckTimeout = TimeSpan.FromSeconds(1) };
        await using var client = new MicsClient(options, () => ws);
        await client.ConnectAsync(new Uri("ws://localhost/ws"), tenantId: "t1", token: "valid:u1", deviceId: "d1", CancellationToken.None);

        var sendTask = client.SendSingleChatAsync(toUserId: "u2", msgBody: new byte[] { 1, 2, 3 }, msgId: "m1", CancellationToken.None).AsTask();

        ws.EnqueueReceive(new ServerFrame
        {
            Ack = new MessageAck { MsgId = "m1", Status = AckStatus.Sent, TimestampMs = 1, Reason = "" }
        }.ToByteArray(), endOfMessage: true);

        var ack = await sendTask;
        Assert.Equal("m1", ack.MsgId);
        Assert.Equal(AckStatus.Sent, ack.Status);

        var clientFrame = ws.Sends
            .Where(s => s.Type == WebSocketMessageType.Binary)
            .Select(s => ClientFrame.Parser.ParseFrom(s.Payload))
            .Single(f => f.PayloadCase == ClientFrame.PayloadOneofCase.Message && f.Message.MsgId == "m1");
        Assert.Equal(ClientFrame.PayloadOneofCase.Message, clientFrame.PayloadCase);
        Assert.Equal("t1", clientFrame.Message.TenantId);
        Assert.Equal("u1", clientFrame.Message.UserId);
        Assert.Equal("d1", clientFrame.Message.DeviceId);
        Assert.Equal("m1", clientFrame.Message.MsgId);
        Assert.Equal(MessageType.SingleChat, clientFrame.Message.MsgType);
        Assert.Equal("u2", clientFrame.Message.ToUserId);
    }

    [Fact]
    public async Task SendSingleChatAsync_EncryptsMsgBody_WhenCryptoConfigured()
    {
        await using var ws = new ScriptedWebSocket();
        ws.EnqueueReceive(new ServerFrame
        {
            ConnectAck = new ConnectAck
            {
                Code = 1000,
                TenantId = "t1",
                UserId = "u1",
                DeviceId = "d1",
                NodeId = "n1",
                TraceId = "tr1",
            }
        }.ToByteArray(), endOfMessage: true);

        var crypto = new Mics.Client.AesGcmMessageCrypto(new byte[32]);
        var options = MicsClientOptions.Default with
        {
            AutoReconnect = false,
            HeartbeatInterval = Timeout.InfiniteTimeSpan,
            AckTimeout = TimeSpan.FromSeconds(1),
            MessageCrypto = crypto
        };
        await using var client = new MicsClient(options, () => ws);
        await client.ConnectAsync(new Uri("ws://localhost/ws"), tenantId: "t1", token: "valid:u1", deviceId: "d1", CancellationToken.None);

        var plaintext = new byte[] { 1, 2, 3, 4 };
        var sendTask = client.SendSingleChatAsync(toUserId: "u2", msgBody: plaintext, msgId: "m-enc", CancellationToken.None).AsTask();
        ws.EnqueueReceive(new ServerFrame
        {
            Ack = new MessageAck { MsgId = "m-enc", Status = AckStatus.Sent, TimestampMs = 1, Reason = "" }
        }.ToByteArray(), endOfMessage: true);
        await sendTask;

        var frame = ws.Sends
            .Where(s => s.Type == WebSocketMessageType.Binary)
            .Select(s => ClientFrame.Parser.ParseFrom(s.Payload))
            .Single(f => f.PayloadCase == ClientFrame.PayloadOneofCase.Message && f.Message.MsgId == "m-enc");
        Assert.Equal("m-enc", frame.Message.MsgId);
        Assert.NotEmpty(frame.Message.MsgBody);
        Assert.NotEqual(ByteString.CopyFrom(plaintext), frame.Message.MsgBody);

        var decrypted = crypto.Decrypt(frame.Message.MsgBody.Span);
        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public async Task DeliveryReceived_DecryptsMsgBody_WhenCryptoConfigured()
    {
        await using var ws = new ScriptedWebSocket();
        ws.EnqueueReceive(new ServerFrame
        {
            ConnectAck = new ConnectAck
            {
                Code = 1000,
                TenantId = "t1",
                UserId = "u1",
                DeviceId = "d1",
                NodeId = "n1",
                TraceId = "tr1",
            }
        }.ToByteArray(), endOfMessage: true);

        var crypto = new Mics.Client.AesGcmMessageCrypto(new byte[32]);
        var options = MicsClientOptions.Default with
        {
            AutoReconnect = false,
            HeartbeatInterval = Timeout.InfiniteTimeSpan,
            MessageCrypto = crypto
        };
        await using var client = new MicsClient(options, () => ws);

        var got = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.DeliveryReceived += d =>
        {
            if (d.Message?.MsgId == "m-del")
            {
                got.TrySetResult(d.Message.MsgBody.ToByteArray());
            }
        };

        await client.ConnectAsync(new Uri("ws://localhost/ws"), tenantId: "t1", token: "valid:u1", deviceId: "d1", CancellationToken.None);

        var plaintext = new byte[] { 9, 8, 7 };
        var ciphertext = crypto.Encrypt(plaintext);
        ws.EnqueueReceive(new ServerFrame
        {
            Delivery = new MessageDelivery
            {
                Message = new MessageRequest
                {
                    TenantId = "t1",
                    UserId = "u2",
                    DeviceId = "d2",
                    MsgId = "m-del",
                    MsgType = MessageType.SingleChat,
                    ToUserId = "u1",
                    MsgBody = ByteString.CopyFrom(ciphertext),
                    TimestampMs = 1,
                }
            }
        }.ToByteArray(), endOfMessage: true);

        var completed = await Task.WhenAny(got.Task, Task.Delay(TimeSpan.FromSeconds(1)));
        Assert.Equal(got.Task, completed);
        Assert.Equal(plaintext, await got.Task);
    }

    [Fact]
    public async Task SendGroupChatAsync_SendsFrame_AndReturnsAck()
    {
        await using var ws = new ScriptedWebSocket();
        ws.EnqueueReceive(new ServerFrame
        {
            ConnectAck = new ConnectAck
            {
                Code = 1000,
                TenantId = "t1",
                UserId = "u1",
                DeviceId = "d1",
                NodeId = "n1",
                TraceId = "tr1",
            }
        }.ToByteArray(), endOfMessage: true);

        var options = MicsClientOptions.Default with { AutoReconnect = false, HeartbeatInterval = Timeout.InfiniteTimeSpan, AckTimeout = TimeSpan.FromSeconds(1) };
        await using var client = new MicsClient(options, () => ws);
        await client.ConnectAsync(new Uri("ws://localhost/ws"), tenantId: "t1", token: "valid:u1", deviceId: "d1", CancellationToken.None);

        var sendTask = client.SendGroupChatAsync(groupId: "group-1", msgBody: new byte[] { 9 }, msgId: "m2", CancellationToken.None).AsTask();

        ws.EnqueueReceive(new ServerFrame
        {
            Ack = new MessageAck { MsgId = "m2", Status = AckStatus.Sent, TimestampMs = 1, Reason = "" }
        }.ToByteArray(), endOfMessage: true);

        var ack = await sendTask;
        Assert.Equal("m2", ack.MsgId);
        Assert.Equal(AckStatus.Sent, ack.Status);

        var clientFrame = ws.Sends
            .Where(s => s.Type == WebSocketMessageType.Binary)
            .Select(s => ClientFrame.Parser.ParseFrom(s.Payload))
            .Single(f => f.PayloadCase == ClientFrame.PayloadOneofCase.Message && f.Message.MsgId == "m2");
        Assert.Equal(ClientFrame.PayloadOneofCase.Message, clientFrame.PayloadCase);
        Assert.Equal(MessageType.GroupChat, clientFrame.Message.MsgType);
        Assert.Equal("group-1", clientFrame.Message.GroupId);
    }

    [Fact]
    public async Task Heartbeat_SendsHeartbeatPingFrames()
    {
        await using var ws = new ScriptedWebSocket();
        ws.EnqueueReceive(new ServerFrame
        {
            ConnectAck = new ConnectAck
            {
                Code = 1000,
                TenantId = "t1",
                UserId = "u1",
                DeviceId = "d1",
                NodeId = "n1",
                TraceId = "tr1",
            }
        }.ToByteArray(), endOfMessage: true);

        var options = MicsClientOptions.Default with { AutoReconnect = false, HeartbeatInterval = TimeSpan.FromMilliseconds(20) };
        await using var client = new MicsClient(options, () => ws);
        await client.ConnectAsync(new Uri("ws://localhost/ws"), tenantId: "t1", token: "valid:u1", deviceId: "d1", CancellationToken.None);

        await Task.Delay(120);

        Assert.Contains(ws.Sends, s =>
        {
            var frame = ClientFrame.Parser.ParseFrom(s.Payload);
            return frame.PayloadCase == ClientFrame.PayloadOneofCase.HeartbeatPing;
        });
    }

    [Fact]
    public async Task SendSingleChatAsync_RetriesUntilAckOrAttemptsExhausted()
    {
        await using var ws = new ScriptedWebSocket();
        ws.EnqueueReceive(new ServerFrame
        {
            ConnectAck = new ConnectAck
            {
                Code = 1000,
                TenantId = "t1",
                UserId = "u1",
                DeviceId = "d1",
                NodeId = "n1",
                TraceId = "tr1",
            }
        }.ToByteArray(), endOfMessage: true);

        var options = MicsClientOptions.Default with
        {
            AutoReconnect = false,
            HeartbeatInterval = Timeout.InfiniteTimeSpan,
            AckTimeout = TimeSpan.FromMilliseconds(50),
            MaxSendAttempts = 2
        };
        await using var client = new MicsClient(options, () => ws);
        await client.ConnectAsync(new Uri("ws://localhost/ws"), tenantId: "t1", token: "valid:u1", deviceId: "d1", CancellationToken.None);

        var sendTask = client.SendSingleChatAsync(toUserId: "u2", msgBody: new byte[] { 1 }, msgId: "m3", CancellationToken.None).AsTask();

        var ok = SpinWait.SpinUntil(() =>
        {
            var sends = ws.Sends
                .Select(s => ClientFrame.Parser.ParseFrom(s.Payload))
                .Count(f => f.PayloadCase == ClientFrame.PayloadOneofCase.Message && f.Message.MsgId == "m3");
            return sends >= 2;
        }, TimeSpan.FromSeconds(1));
        Assert.True(ok);

        ws.EnqueueReceive(new ServerFrame
        {
            Ack = new MessageAck { MsgId = "m3", Status = AckStatus.Sent, TimestampMs = 1, Reason = "" }
        }.ToByteArray(), endOfMessage: true);

        var ack = await sendTask;
        Assert.Equal(AckStatus.Sent, ack.Status);

        var messageSends = ws.Sends
            .Select(s => ClientFrame.Parser.ParseFrom(s.Payload))
            .Count(f => f.PayloadCase == ClientFrame.PayloadOneofCase.Message && f.Message.MsgId == "m3");
        Assert.Equal(2, messageSends);
    }

    [Fact]
    public async Task AutoReconnect_ReconnectsAfterSocketClose()
    {
        await using var ws1 = new ScriptedWebSocket();
        await using var ws2 = new ScriptedWebSocket();

        ws1.EnqueueReceive(new ServerFrame
        {
            ConnectAck = new ConnectAck
            {
                Code = 1000,
                TenantId = "t1",
                UserId = "u1",
                DeviceId = "d1",
                NodeId = "n1",
                TraceId = "tr1",
            }
        }.ToByteArray(), endOfMessage: true);

        ws2.EnqueueReceive(new ServerFrame
        {
            ConnectAck = new ConnectAck
            {
                Code = 1000,
                TenantId = "t1",
                UserId = "u1",
                DeviceId = "d1",
                NodeId = "n2",
                TraceId = "tr2",
            }
        }.ToByteArray(), endOfMessage: true);

        var factoryCalls = 0;
        var sockets = new Queue<IMicsWebSocket>(new IMicsWebSocket[] { ws1, ws2 });
        IMicsWebSocket Factory()
        {
            Interlocked.Increment(ref factoryCalls);
            return sockets.Dequeue();
        }

        var options = MicsClientOptions.Default with
        {
            AutoReconnect = true,
            HeartbeatInterval = Timeout.InfiniteTimeSpan,
            ReconnectMinDelay = TimeSpan.FromMilliseconds(10),
            ReconnectMaxDelay = TimeSpan.FromMilliseconds(10),
            ConnectTimeout = TimeSpan.FromSeconds(1),
        };
        await using var client = new MicsClient(options, Factory);

        var connected = 0;
        var reconnected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.Connected += s =>
        {
            if (Interlocked.Increment(ref connected) == 2)
            {
                reconnected.TrySetResult(true);
            }
        };

        await client.ConnectAsync(new Uri("ws://localhost/ws"), tenantId: "t1", token: "valid:u1", deviceId: "d1", CancellationToken.None);

        ws1.CompleteReceives();

        var completed = await Task.WhenAny(reconnected.Task, Task.Delay(TimeSpan.FromSeconds(1)));
        Assert.Equal(reconnected.Task, completed);
    }

    [Fact]
    public async Task SendWhileReconnecting_WaitsForReconnect_AndSendsOnNewSocket()
    {
        await using var ws1 = new ScriptedWebSocket();
        await using var ws2 = new ScriptedWebSocket();

        ws1.EnqueueReceive(new ServerFrame
        {
            ConnectAck = new ConnectAck
            {
                Code = 1000,
                TenantId = "t1",
                UserId = "u1",
                DeviceId = "d1",
                NodeId = "n1",
                TraceId = "tr1",
            }
        }.ToByteArray(), endOfMessage: true);

        ws2.EnqueueReceive(new ServerFrame
        {
            ConnectAck = new ConnectAck
            {
                Code = 1000,
                TenantId = "t1",
                UserId = "u1",
                DeviceId = "d1",
                NodeId = "n2",
                TraceId = "tr2",
            }
        }.ToByteArray(), endOfMessage: true);

        var factoryCalls = 0;
        var sockets = new Queue<IMicsWebSocket>(new IMicsWebSocket[] { ws1, ws2 });
        IMicsWebSocket Factory()
        {
            Interlocked.Increment(ref factoryCalls);
            return sockets.Dequeue();
        }

        var options = MicsClientOptions.Default with
        {
            AutoReconnect = true,
            HeartbeatInterval = Timeout.InfiniteTimeSpan,
            ReconnectMinDelay = TimeSpan.FromMilliseconds(10),
            ReconnectMaxDelay = TimeSpan.FromMilliseconds(10),
            ConnectTimeout = TimeSpan.FromSeconds(1),
            AckTimeout = TimeSpan.FromMilliseconds(200),
            MaxSendAttempts = 1
        };
        await using var client = new MicsClient(options, Factory);
        await client.ConnectAsync(new Uri("ws://localhost/ws"), tenantId: "t1", token: "valid:u1", deviceId: "d1", CancellationToken.None);

        ws1.CompleteReceives(); // triggers reconnect

        // First wait until reconnect actually happened (second socket created), then wait for the send.
        var reconnected = await WaitUntilAsync(() => Volatile.Read(ref factoryCalls) >= 2, TimeSpan.FromSeconds(5));
        Assert.True(reconnected);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var sendTask = client.SendSingleChatAsync(toUserId: "u2", msgBody: new byte[] { 1 }, msgId: "m-reconn", cts.Token).AsTask();

        // Wait until the reconnected socket actually sends the message, then inject the Ack.
        var sentOnWs2 = await WaitUntilAsync(() =>
        {
            return ws2.Sends.Any(s =>
            {
                var frame = ClientFrame.Parser.ParseFrom(s.Payload);
                return frame.PayloadCase == ClientFrame.PayloadOneofCase.Message && frame.Message.MsgId == "m-reconn";
            });
        }, TimeSpan.FromSeconds(5));
        Assert.True(sentOnWs2);

        ws2.EnqueueReceive(new ServerFrame
        {
            Ack = new MessageAck { MsgId = "m-reconn", Status = AckStatus.Sent, TimestampMs = 1, Reason = "" }
        }.ToByteArray(), endOfMessage: true);

        var ack = await sendTask;
        Assert.Equal(AckStatus.Sent, ack.Status);
        Assert.Contains(ws2.Sends, s =>
        {
            var frame = ClientFrame.Parser.ParseFrom(s.Payload);
            return frame.PayloadCase == ClientFrame.PayloadOneofCase.Message && frame.Message.MsgId == "m-reconn";
        });

        static async Task<bool> WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.Elapsed < timeout)
            {
                if (predicate())
                {
                    return true;
                }

                await Task.Delay(5, CancellationToken.None);
            }

            return predicate();
        }
    }

    private sealed class ScriptedWebSocket : IMicsWebSocket
    {
        private readonly Channel<(byte[] Payload, bool EndOfMessage)> _receives = Channel.CreateUnbounded<(byte[], bool)>();
        private readonly List<(WebSocketMessageType Type, byte[] Payload)> _sends = new();
        private readonly object _lock = new();
        private WebSocketState _state = WebSocketState.Open;

        public WebSocketState State => _state;

        public IReadOnlyList<(WebSocketMessageType Type, byte[] Payload)> Sends
        {
            get
            {
                lock (_lock)
                {
                    return _sends.ToArray();
                }
            }
        }

        public void EnqueueReceive(byte[] payload, bool endOfMessage) =>
            _receives.Writer.TryWrite((payload, endOfMessage));

        public void CompleteReceives()
        {
            _state = WebSocketState.CloseReceived;
            _receives.Writer.TryComplete();
        }

        public ValueTask ConnectAsync(Uri uri, CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
        {
            _state = WebSocketState.Closed;
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            _state = WebSocketState.Closed;
            _receives.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }

        public async ValueTask<ValueWebSocketReceiveResult> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            (byte[] Payload, bool EndOfMessage) next;
            try
            {
                next = await _receives.Reader.ReadAsync(cancellationToken);
            }
            catch (ChannelClosedException)
            {
                _state = WebSocketState.CloseReceived;
                return new ValueWebSocketReceiveResult(0, WebSocketMessageType.Close, true);
            }

            next.Payload.CopyTo(buffer);
            return new ValueWebSocketReceiveResult(next.Payload.Length, WebSocketMessageType.Binary, next.EndOfMessage);
        }

        public ValueTask SendAsync(ReadOnlyMemory<byte> buffer, WebSocketMessageType messageType, WebSocketMessageFlags messageFlags, CancellationToken cancellationToken)
        {
            lock (_lock)
            {
                _sends.Add((messageType, buffer.ToArray()));
            }
            return ValueTask.CompletedTask;
        }
    }
}
