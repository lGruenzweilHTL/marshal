using System.Net.WebSockets;
using Microsoft.AspNetCore.TestHost;

namespace Marshal.Integrations.Moonraker.Tests;

/// <summary>Connects websockets to a <see cref="TestServer"/>, sending the API key header like the real factory.</summary>
internal sealed class TestServerSockets(TestServer server) : IMoonrakerSocketFactory
{
    public Task<WebSocket> ConnectAsync(Uri uri, string? apiKey, CancellationToken cancellationToken)
    {
        WebSocketClient client = server.CreateWebSocketClient();
        if (apiKey is not null)
            client.ConfigureRequest = request => request.Headers["X-Api-Key"] = apiKey;

        return client.ConnectAsync(uri, cancellationToken);
    }
}
