using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Marshal.Api.Tests.Http;

public sealed class SpaHostingTests
{
    private const string Index = """
        <!doctype html>
        <html><head><base href="/" /><script type="module" src="./assets/app.js"></script></head>
        <body><div id="app">marshal-spa</div></body></html>
        """;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task DeepLink_ReturnsTheEntryPageWithCsp()
    {
        using MarshalApiFactory factory = await WithSpaAsync();

        HttpResponseMessage response = await factory.CreateClient().GetAsync("/runs/123", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("marshal-spa", await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        string csp = string.Join(";", response.Headers.GetValues("Content-Security-Policy"));
        Assert.Contains("img-src 'self' data:", csp, StringComparison.Ordinal);
        Assert.Contains("script-src 'self'", csp, StringComparison.Ordinal);
        Assert.Contains("style-src 'self'", csp, StringComparison.Ordinal);
        Assert.Contains("frame-src 'self'", csp, StringComparison.Ordinal);
        Assert.Equal("no-cache", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Root_ReturnsTheEntryPage()
    {
        using MarshalApiFactory factory = await WithSpaAsync();

        string body = await factory.CreateClient().GetStringAsync("/", Ct);

        Assert.Contains("marshal-spa", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Asset_IsServedAsFileAndMissingAssetIs404()
    {
        using MarshalApiFactory factory = await WithSpaAsync();
        HttpClient client = factory.CreateClient();

        HttpResponseMessage asset = await client.GetAsync("/assets/app.js", Ct);
        HttpResponseMessage missing = await client.GetAsync("/assets/missing.js", Ct);

        Assert.Equal(HttpStatusCode.OK, asset.StatusCode);
        Assert.Equal("console.log(1)", await asset.Content.ReadAsStringAsync(Ct));
        Assert.Contains("'self'", string.Join(";", asset.Headers.GetValues("Content-Security-Policy")), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task PathBase_IsWrittenIntoTheBaseElement()
    {
        using MarshalApiFactory factory = await WithSpaAsync(new() { ["http:pathBase"] = "/marshal" });
        HttpClient client = factory.CreateClient();

        string body = await client.GetStringAsync("/marshal/runs/1", Ct);
        HttpResponseMessage asset = await client.GetAsync("/marshal/assets/app.js", Ct);

        Assert.Contains("<base href=\"/marshal/\" />", body, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, asset.StatusCode);
    }

    [Fact]
    public async Task UnknownApiPath_StaysAJsonProblem()
    {
        using MarshalApiFactory factory = await WithSpaAsync();

        HttpResponseMessage response = await factory.CreateClient().GetAsync("/api/v1/nope", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        JsonElement problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("api.unsupported_version", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Post_ToAnSpaPath_IsNotTheEntryPage()
    {
        using MarshalApiFactory factory = await WithSpaAsync();

        HttpResponseMessage response = await factory.CreateClient().PostAsync("/runs/1", null, Ct);

        Assert.DoesNotContain("marshal-spa", await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithoutBuiltSpa_DeepLinkIs404()
    {
        using var factory = new MarshalApiFactory();

        HttpResponseMessage response = await factory.CreateClient().GetAsync("/runs/1", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task<MarshalApiFactory> WithSpaAsync(Dictionary<string, string?>? settings = null)
    {
        var factory = new MarshalApiFactory(settings);
        await File.WriteAllTextAsync(Path.Combine(factory.WebRoot, "index.html"), Index, Ct);
        Directory.CreateDirectory(Path.Combine(factory.WebRoot, "assets"));
        await File.WriteAllTextAsync(Path.Combine(factory.WebRoot, "assets", "app.js"), "console.log(1)", Ct);
        return factory;
    }
}
