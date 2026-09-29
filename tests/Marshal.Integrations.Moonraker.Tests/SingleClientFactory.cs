namespace Marshal.Integrations.Moonraker.Tests;

/// <summary>Hands out one pre-built <see cref="HttpClient"/> (e.g. a <c>TestServer</c> client).</summary>
internal sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => client;
}
