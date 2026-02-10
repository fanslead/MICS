using System.Net.WebSockets;

namespace Mics.Client;

internal sealed class ClientWebSocketAdapter : IMicsWebSocket
{
    private readonly ClientWebSocket _ws;

    public ClientWebSocketAdapter()
    {
        _ws = new ClientWebSocket();
        _ws.Options.KeepAliveInterval = TimeSpan.Zero;
        _ws.Options.SetBuffer(8 * 1024, 8 * 1024);
    }

    public WebSocketState State => _ws.State;

    public async ValueTask ConnectAsync(Uri uri, CancellationToken cancellationToken) =>
        await _ws.ConnectAsync(uri, cancellationToken);

    public async ValueTask<ValueWebSocketReceiveResult> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken) =>
        await _ws.ReceiveAsync(buffer, cancellationToken);

    public async ValueTask SendAsync(ReadOnlyMemory<byte> buffer, WebSocketMessageType messageType, WebSocketMessageFlags messageFlags, CancellationToken cancellationToken) =>
        await _ws.SendAsync(buffer, messageType, messageFlags, cancellationToken);

    public async ValueTask CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) =>
        await _ws.CloseAsync(closeStatus, statusDescription, cancellationToken);

    public ValueTask DisposeAsync()
    {
        _ws.Dispose();
        return ValueTask.CompletedTask;
    }
}

