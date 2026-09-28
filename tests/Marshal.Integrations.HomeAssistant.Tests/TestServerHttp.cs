using Microsoft.AspNetCore.TestHost;

namespace Marshal.Integrations.HomeAssistant.Tests;

/// <summary>HTTP clients that talk to a <see cref="TestServer"/>.</summary>
internal sealed class TestServerHttp(TestServer server) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(server.CreateHandler(), disposeHandler: false) { BaseAddress = server.BaseAddress };
}
