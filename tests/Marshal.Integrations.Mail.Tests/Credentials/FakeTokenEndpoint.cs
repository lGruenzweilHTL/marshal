using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace Marshal.Integrations.Mail.Tests.Credentials;

/// <summary>An OAuth token endpoint: records form posts and answers with queued responses.</summary>
internal sealed class FakeTokenEndpoint : HttpMessageHandler, IHttpClientFactory
{
    private readonly Queue<(HttpStatusCode Status, JsonObject Body)> _responses = new();

    public List<(Uri Url, Dictionary<string, string> Form)> Requests { get; } = [];

    public FakeTokenEndpoint Returns(string accessToken, int expiresIn = 3600, string? refreshToken = null)
    {
        var body = new JsonObject { ["access_token"] = accessToken, ["token_type"] = "Bearer", ["expires_in"] = expiresIn };
        if (refreshToken is not null)
            body["refresh_token"] = refreshToken;
        _responses.Enqueue((HttpStatusCode.OK, body));
        return this;
    }

    public FakeTokenEndpoint Fails(string error, HttpStatusCode status = HttpStatusCode.BadRequest)
    {
        _responses.Enqueue((status, new JsonObject { ["error"] = error, ["error_description"] = $"{error} description" }));
        return this;
    }

    public HttpClient CreateClient(string name) => new(this, disposeHandler: false);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string form = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request.RequestUri!, form.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(pair => Uri.UnescapeDataString(pair[0]), pair => Uri.UnescapeDataString(pair[1].Replace('+', ' ')))));
        (HttpStatusCode status, JsonObject body) = _responses.Dequeue();
        return new HttpResponseMessage(status) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
    }
}
