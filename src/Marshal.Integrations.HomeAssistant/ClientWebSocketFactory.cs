using System.Net.WebSockets;

namespace Marshal.Integrations.HomeAssistant;

/// <summary>
/// Real websockets with keep-alive pings, so a dead connection is noticed.
/// </summary>
public sealed class ClientWebSocketFactory : IHomeAssistantSocketFactory
{
    public async Task<WebSocket> ConnectAsync(Uri uri, CancellationToken cancellationToken)
    {
        var socket = new ClientWebSocket();
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
        socket.Options.KeepAliveTimeout = TimeSpan.FromSeconds(20);
        try
        {
            await socket.ConnectAsync(uri, cancellationToken);
            return socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
