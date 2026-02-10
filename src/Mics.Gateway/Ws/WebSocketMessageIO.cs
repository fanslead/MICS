using System.Buffers;
using System.IO.Pipelines;
using System.Net.WebSockets;
using Google.Protobuf;
using Mics.Gateway.Protocol;

namespace Mics.Gateway.Ws;

internal static class WebSocketMessageIO
{
    public static async ValueTask<PooledProtobufBytes?> ReadBinaryAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var pipe = new Pipe(new PipeOptions(
            pool: MemoryPool<byte>.Shared,
            minimumSegmentSize: 4 * 1024,
            pauseWriterThreshold: 256 * 1024,
            resumeWriterThreshold: 128 * 1024,
            useSynchronizationContext: false));

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var mem = pipe.Writer.GetMemory(8 * 1024);
                var res = await socket.ReceiveAsync(mem, cancellationToken);

                if (res.MessageType == WebSocketMessageType.Close)
                {
                    return null;
                }

                if (res.MessageType != WebSocketMessageType.Binary)
                {
                    // Drain non-binary messages without buffering.
                    if (res.EndOfMessage)
                    {
                        continue;
                    }

                    while (!res.EndOfMessage)
                    {
                        res = await socket.ReceiveAsync(mem, cancellationToken);
                        if (res.MessageType == WebSocketMessageType.Close)
                        {
                            return null;
                        }
                    }

                    continue;
                }

                pipe.Writer.Advance(res.Count);
                await pipe.Writer.FlushAsync(cancellationToken);

                if (!res.EndOfMessage)
                {
                    continue;
                }

                var read = await pipe.Reader.ReadAsync(cancellationToken);
                var buffer = read.Buffer;
                var len = checked((int)buffer.Length);

                var rented = ArrayPool<byte>.Shared.Rent(Math.Max(1, len));
                buffer.CopyTo(rented);
                pipe.Reader.AdvanceTo(buffer.End);

                return new PooledProtobufBytes(rented, len);
            }

            return null;
        }
        finally
        {
            await pipe.Reader.CompleteAsync();
            await pipe.Writer.CompleteAsync();
        }
    }

    public static async ValueTask SendMessageAsync(WebSocket socket, IMessage message, CancellationToken cancellationToken)
    {
        using var bytes = PooledProtobufSerializer.Serialize(message);
        await socket.SendAsync(bytes.Memory, WebSocketMessageType.Binary, true, cancellationToken);
    }
}
