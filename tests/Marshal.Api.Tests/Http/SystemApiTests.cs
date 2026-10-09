using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Marshal.Abstractions.Tokens;
using Marshal.Api.Tests.Auth;

namespace Marshal.Api.Tests.Http;

public sealed class SystemApiTests : IAsyncLifetime
{
    private readonly MarshalApiFactory _factory = new(AuthFactory.Local().ToSettings(0));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task System_SignedIn_ShowsAppVersionAndApiVersions()
    {
        (HttpClient browser, _) = await Browser.SignInAsync(_factory, "user");

        JsonElement system = await browser.GetFromJsonAsync<JsonElement>("/api/v1/system", Ct);

        Assert.False(string.IsNullOrWhiteSpace(system.GetProperty("version").GetString()));
        Assert.Contains("1.0", system.GetProperty("supportedApiVersions").EnumerateArray().Select(version => version.GetString()));
        Assert.Equal(JsonValueKind.Array, system.GetProperty("deprecatedApiVersions").ValueKind);
    }

    [Fact]
    public async Task System_ListsUptimeKumaWithItsLastReceivedTime()
    {
        (HttpClient browser, _) = await Browser.SignInAsync(_factory, "user");
        static JsonElement Kuma(JsonElement system) => system.GetProperty("eventSources").EnumerateArray().Single(source => source.GetProperty("source").GetString() == "uptime-kuma");

        JsonElement before = Kuma(await browser.GetFromJsonAsync<JsonElement>("/api/v1/system", Ct));
        HttpClient hook = await _factory.ClientWithTokenAsync(ApiScopes.HooksUptimeKuma);
        DateTimeOffset sent = DateTimeOffset.UtcNow;
        var test = new StringContent("""{"heartbeat":null,"monitor":null,"msg":"Testing"}""", System.Text.Encoding.UTF8, "application/json");
        Assert.Equal(HttpStatusCode.Accepted, (await hook.PostAsync("/api/v1/hooks/uptime-kuma", test, Ct)).StatusCode);
        JsonElement after = Kuma(await browser.GetFromJsonAsync<JsonElement>("/api/v1/system", Ct));

        Assert.Equal((false, JsonValueKind.Null), (before.GetProperty("connected").GetBoolean(), before.GetProperty("lastReceivedAt").ValueKind));
        Assert.True(after.GetProperty("connected").GetBoolean());
        Assert.True(after.GetProperty("webhook").GetBoolean());
        Assert.InRange(after.GetProperty("lastReceivedAt").GetDateTimeOffset(), sent.AddSeconds(-1), DateTimeOffset.UtcNow.AddSeconds(1));
    }

    [Fact]
    public async Task System_Token_IsAllowed()
    {
        HttpClient client = await _factory.ClientWithTokenAsync(ApiScopes.TasksRead);

        HttpResponseMessage response = await client.GetAsync("/api/v1/system", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task System_Anonymous_Returns401()
    {
        HttpResponseMessage response = await _factory.CreateClient().GetAsync("/api/v1/system", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
