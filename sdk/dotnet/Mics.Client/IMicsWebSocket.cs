using System.Net.WebSockets;

namespace Mics.Client;

internal interface IMicsWebSocket : IAsyncDisposable
{
    WebSocketState State { get; }

    ValueTask ConnectAsync(Uri uri, CancellationToken cancellationToken);
    ValueTask<ValueWebSocketReceiveResult> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken);
    ValueTask SendAsync(ReadOnlyMemory<byte> buffer, WebSocketMessageType messageType, WebSocketMessageFlags messageFlags, CancellationToken cancellationToken);
    ValueTask CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken);
}
