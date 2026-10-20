using System.Net;
using System.Text;
using Marshal.Abstractions;
using Marshal.Abstractions.Web;
using Microsoft.Extensions.Options;

namespace Marshal.Integrations.Web.Tests;

public sealed class SearxngSearchProviderTests
{
    private const string Results = """
        {"query":"christmas market","results":[
          {"title":"Christmas market Linz","content":"Opens on 20 November.","url":"https://www.linztourismus.at/maerkte","engines":["google"]},
          {"title":"No link","content":"x"},
          {"title":"Local file","content":"x","url":"file:///etc/passwd"},
          {"title":"Wiki","content":"A market.","url":"https://de.wikipedia.org/wiki/Weihnachtsmarkt"},
          {"title":"Third","content":"Three.","url":"https://example.org/3"}]}
        """;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Search_OneJsonRequestWithEnginesLanguageAndQuery()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, Results);
        SearxngSearchProvider provider = Provider(handler, new SearxngOptions { Url = new Uri("http://searxng.lan:8080/"), Engines = ["google", "wikipedia"] });

        Result<IReadOnlyList<WebResult>> results = await provider.SearchAsync("christmas market & linz", "de", 8, Ct);

        HttpRequestMessage request = Assert.Single(handler.Requests);
        Assert.Equal("http://searxng.lan:8080/search?q=christmas%20market%20%26%20linz&format=json&engines=google%2Cwikipedia&language=de", request.RequestUri!.AbsoluteUri);
        Assert.False(request.Headers.Contains("Authorization"));
        Assert.Equal(
            [("Christmas market Linz", "Opens on 20 November.", "linztourismus.at"), ("Wiki", "A market.", "de.wikipedia.org"), ("Third", "Three.", "example.org")],
            results.Value.Select(result => (result.Title, result.Snippet, result.Domain)));
    }

    [Fact]
    public async Task Search_CapsAtTheLimit()
    {
        SearxngSearchProvider provider = Provider(new RecordingHandler(HttpStatusCode.OK, Results), new SearxngOptions { Url = new Uri("http://searxng.lan") });

        Assert.Single((await provider.SearchAsync("x", "en", 1, Ct)).Value);
    }

    [Fact]
    public async Task Search_SendsTheAuthHeaderFromTheSecret()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, Results);
        SearxngSearchProvider provider = Provider(handler, new SearxngOptions { Url = new Uri("http://searxng.lan"), AuthHeaderSecret = "searxng_auth", AuthHeader = "Basic bWFyc2hhbDpzZWNyZXQ=" });

        await provider.SearchAsync("x", "en", 8, Ct);

        Assert.Equal("Basic bWFyc2hhbDpzZWNyZXQ=", Assert.Single(handler.Requests).Headers.GetValues("Authorization").Single());
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, "{}")]
    [InlineData(HttpStatusCode.OK, "<html>no json</html>")]
    public async Task Search_FailingServiceIsUnavailable(HttpStatusCode status, string body)
    {
        SearxngSearchProvider provider = Provider(new RecordingHandler(status, body), new SearxngOptions { Url = new Uri("http://searxng.lan") });

        Result<IReadOnlyList<WebResult>> results = await provider.SearchAsync("x", "en", 8, Ct);

        Assert.Equal("web.unavailable", results.Error.Code);
    }

    [Fact]
    public async Task Search_TimeoutIsUnavailable()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, Results) { Throw = new TaskCanceledException("timeout") };
        SearxngSearchProvider provider = Provider(handler, new SearxngOptions { Url = new Uri("http://searxng.lan") });

        Assert.Equal("web.unavailable", (await provider.SearchAsync("x", "en", 8, Ct)).Error.Code);
    }

    private static SearxngSearchProvider Provider(HttpMessageHandler handler, SearxngOptions options) =>
        new(new HandlerClientFactory(handler), Options.Create(options));

    private sealed class HandlerClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class RecordingHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        public Exception? Throw { get; init; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (Throw is not null)
                throw Throw;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}
