namespace Marshal.Integrations.FreshRss.Tests;

/// <summary>Plain <see cref="HttpClient"/>s, optionally over one handler.</summary>
internal sealed class PlainHttpClientFactory(HttpMessageHandler? handler = null) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
}
