using System.Buffers;
using System.Net.WebSockets;
using Google.Protobuf;
using Mics.Contracts.Message.V1;

namespace Mics.Tests;

public sealed class PipelinesIoTests
{
    private sealed class ScriptedWebSocket : WebSocket
    {
        private readonly Queue<(WebSocketMessageType Type, byte[] Payload, bool EndOfMessage)> _receives = new();
        private readonly List<(WebSocketMessageType Type, byte[] Payload, bool EndOfMessage)> _sends = new();
        private WebSocketState _state = WebSocketState.Open;

        public IReadOnlyList<(WebSocketMessageType Type, byte[] Payload, bool EndOfMessage)> Sends => _sends;

        public void EnqueueReceive(WebSocketMessageType type, byte[] payload, bool endOfMessage) =>
            _receives.Enqueue((type, payload, endOfMessage));

        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State => _state;
        public override string? SubProtocol => null;

        public override void Abort() => _state = WebSocketState.Aborted;

        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
        {
            _state = WebSocketState.Closed;
            return Task.CompletedTask;
        }

        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
        {
            _state = WebSocketState.CloseSent;
            return Task.CompletedTask;
        }

        public override void Dispose() => _state = WebSocketState.Closed;

        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken) =>
            ReceiveAsync(buffer.AsMemory(), cancellationToken).AsTask().ContinueWith(
                t => new WebSocketReceiveResult(t.Result.Count, t.Result.MessageType, t.Result.EndOfMessage),
                cancellationToken);

        public override ValueTask<ValueWebSocketReceiveResult> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            if (_receives.Count == 0)
            {
                _state = WebSocketState.CloseReceived;
                return new ValueTask<ValueWebSocketReceiveResult>(new ValueWebSocketReceiveResult(0, WebSocketMessageType.Close, true));
            }

            var (type, payload, eom) = _receives.Dequeue();
            payload.CopyTo(buffer);
            return new ValueTask<ValueWebSocketReceiveResult>(new ValueWebSocketReceiveResult(payload.Length, type, eom));
        }

        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken) =>
            SendAsync(buffer.AsMemory(), messageType, endOfMessage ? WebSocketMessageFlags.EndOfMessage : WebSocketMessageFlags.None, cancellationToken).AsTask();

        public override ValueTask SendAsync(ReadOnlyMemory<byte> buffer, WebSocketMessageType messageType, WebSocketMessageFlags messageFlags, CancellationToken cancellationToken)
        {
            _sends.Add((messageType, buffer.ToArray(), (messageFlags & WebSocketMessageFlags.EndOfMessage) != 0));
            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task WebSocketMessageIO_ReadBinaryAsync_ReassemblesFragments()
    {
        var asm = typeof(Mics.Gateway.Ws.WsGatewayHandler).Assembly;
        var ioType = asm.GetType("Mics.Gateway.Ws.WebSocketMessageIO", throwOnError: false);
        Assert.NotNull(ioType);

        var read = ioType!.GetMethod("ReadBinaryAsync");
        Assert.NotNull(read);

        var frame = new ClientFrame
        {
            HeartbeatPing = new HeartbeatPing { TimestampMs = 123 },
        };
        var bytes = frame.ToByteArray();
        var a = bytes[..Math.Min(3, bytes.Length)];
        var b = bytes[Math.Min(3, bytes.Length)..];

        using var ws = new ScriptedWebSocket();
        ws.EnqueueReceive(WebSocketMessageType.Binary, a, endOfMessage: false);
        ws.EnqueueReceive(WebSocketMessageType.Binary, b, endOfMessage: true);

        dynamic vt = read!.Invoke(null, new object[] { ws, CancellationToken.None })!;
        object? pooled = await vt;
        Assert.NotNull(pooled);

        var pooledType = pooled!.GetType();
        var buffer = (byte[])pooledType.GetProperty("Buffer")!.GetValue(pooled)!;
        var length = (int)pooledType.GetProperty("Length")!.GetValue(pooled)!;

        var cis = new CodedInputStream(buffer, 0, length);
        var parsed = ClientFrame.Parser.ParseFrom(cis);
        Assert.Equal(frame, parsed);

        ((IDisposable)pooled).Dispose();
    }

    [Fact]
    public async Task PooledProtobufSerializer_SerializesWithoutExtraBytes()
    {
        var asm = typeof(Mics.Gateway.Ws.WsGatewayHandler).Assembly;
        var serializerType = asm.GetType("Mics.Gateway.Protocol.PooledProtobufSerializer", throwOnError: false);
        Assert.NotNull(serializerType);

        var serialize = serializerType!.GetMethod("Serialize");
        Assert.NotNull(serialize);

        var frame = new ServerFrame
        {
            ConnectAck = new ConnectAck { Code = 1000, TenantId = "t1", UserId = "u1", DeviceId = "d1", NodeId = "n1", TraceId = "tr" }
        };

        var pooled = serialize!.Invoke(null, new object[] { frame })!;
        var pooledType = pooled.GetType();
        var buffer = (byte[])pooledType.GetProperty("Buffer")!.GetValue(pooled)!;
        var length = (int)pooledType.GetProperty("Length")!.GetValue(pooled)!;

        var cis = new CodedInputStream(buffer, 0, length);
        var parsed = ServerFrame.Parser.ParseFrom(cis);
        Assert.Equal(frame, parsed);

        ((IDisposable)pooled).Dispose();
    }
}
