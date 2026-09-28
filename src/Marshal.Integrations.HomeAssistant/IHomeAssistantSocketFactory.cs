using System.Net.WebSockets;

namespace Marshal.Integrations.HomeAssistant;

/// <summary>
/// Opens the websocket to Home Assistant.
/// </summary>
public interface IHomeAssistantSocketFactory
{
    /// <summary>A connected websocket to <paramref name="uri"/>.</summary>
    Task<WebSocket> ConnectAsync(Uri uri, CancellationToken cancellationToken);
}
