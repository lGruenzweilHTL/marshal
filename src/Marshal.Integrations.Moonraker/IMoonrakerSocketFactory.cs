using System.Net.WebSockets;

namespace Marshal.Integrations.Moonraker;

/// <summary>Opens websockets to Moonraker (replaced by an in-process server in tests).</summary>
public interface IMoonrakerSocketFactory
{
    /// <summary>A connected websocket to <paramref name="uri"/>, with the <c>X-Api-Key</c> header when a key is given.</summary>
    Task<WebSocket> ConnectAsync(Uri uri, string? apiKey, CancellationToken cancellationToken);
}

/// <summary>Real websockets.</summary>
internal sealed class ClientWebSocketFactory : IMoonrakerSocketFactory
{
    public async Task<WebSocket> ConnectAsync(Uri uri, string? apiKey, CancellationToken cancellationToken)
    {
        var socket = new ClientWebSocket();
        if (apiKey is not null)
            socket.Options.SetRequestHeader("X-Api-Key", apiKey);

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
