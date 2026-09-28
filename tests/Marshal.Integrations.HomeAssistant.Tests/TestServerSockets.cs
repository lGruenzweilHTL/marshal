using System.Net.WebSockets;
using Microsoft.AspNetCore.TestHost;

namespace Marshal.Integrations.HomeAssistant.Tests;

/// <summary>Connects websockets to a <see cref="TestServer"/>.</summary>
internal sealed class TestServerSockets(TestServer server) : IHomeAssistantSocketFactory
{
    public List<Uri> Uris { get; } = [];

    public Task<WebSocket> ConnectAsync(Uri uri, CancellationToken cancellationToken)
    {
        Uris.Add(uri);
        return server.CreateWebSocketClient().ConnectAsync(uri, cancellationToken);
    }
}
