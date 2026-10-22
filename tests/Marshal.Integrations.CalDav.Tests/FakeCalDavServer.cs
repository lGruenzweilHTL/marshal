using System.Net;
using System.Text;

namespace Marshal.Integrations.CalDav.Tests;

/// <summary>
/// A CalDAV server as a message handler: canned responses per method and absolute URL, Basic auth checked against
/// one user, every request recorded with its body.
/// </summary>
internal sealed class FakeCalDavServer : HttpMessageHandler
{
    private readonly Dictionary<(string Method, string Url), Func<string, Response>> _routes = [];

    public string Credentials { get; set; } = "lukas:secret";

    public List<Recorded> Requests { get; } = [];

    public FakeCalDavServer On(string method, string url, HttpStatusCode status, string body = "", string? etag = null)
    {
        _routes[(method, url)] = _ => new Response(status, body, etag);
        return this;
    }

    public FakeCalDavServer On(string method, string url, Func<string, Response> respond)
    {
        _routes[(method, url)] = respond;
        return this;
    }

    public FakeCalDavServer OnFixture(string method, string url, string fixture) =>
        On(method, url, (HttpStatusCode)207, Fixture(fixture));

    public static string Fixture(string path) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", path));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new Recorded(request.Method.Method, request.RequestUri!.AbsoluteUri, request.Headers.ToDictionary(header => header.Key, header => string.Join(",", header.Value)), body));

        string expected = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(Credentials));
        if (request.Headers.Authorization?.ToString() != expected)
            return new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("") };

        if (!_routes.TryGetValue((request.Method.Method, request.RequestUri.AbsoluteUri), out Func<string, Response>? respond))
            return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("") };

        Response response = respond(body);
        var message = new HttpResponseMessage(response.Status) { Content = new StringContent(response.Body, Encoding.UTF8, response.Body.StartsWith("BEGIN:", StringComparison.Ordinal) ? "text/calendar" : "application/xml") };
        if (response.ETag is not null)
            message.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue(response.ETag);
        return message;
    }

    internal sealed record Response(HttpStatusCode Status, string Body, string? ETag = null);

    internal sealed record Recorded(string Method, string Url, Dictionary<string, string> Headers, string Body);
}

internal sealed class HandlerClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}
