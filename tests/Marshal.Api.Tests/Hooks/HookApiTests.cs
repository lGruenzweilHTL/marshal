using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Marshal.Abstractions.Events;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Tokens;
using Marshal.Api.Tests.Http;
using Marshal.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Marshal.Api.Tests.Hooks;

public sealed class HookApiTests : IDisposable
{
    private const string Kuma = "/api/v1/hooks/uptime-kuma";

    private readonly FakeEventPublisher _events = new();
    private readonly MarshalApiFactory _factory;

    public HookApiTests()
    {
        _factory = new MarshalApiFactory(new Dictionary<string, string?> { ["webhooks:requestsPerMinute"] = "5" })
        {
            TestServices = services =>
            {
                services.RemoveAll<IEventPublisher>();
                services.AddSingleton<IEventPublisher>(_events);
            },
        };
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Post_KumaDown_Returns202AndPublishesTheOwnersTrustedEvent()
    {
        HttpClient client = await _factory.ClientWithTokenAsync(ApiScopes.HooksUptimeKuma);

        HttpResponseMessage response = await client.PostAsync(Kuma, Json("down"), Ct);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        MarshalEvent @event = Assert.Single(_events.Events);
        OwnerId owner = await OwnerAsync();
        Assert.Equal(("uptime-kuma", "monitor.down", "NAS", (OwnerId?)owner, true), (@event.Source, @event.Type, @event.Entity, @event.OwnerId, @event.Trusted));
    }

    [Fact]
    public async Task Post_KumaDownAsFormData_Returns202AndPublishes()
    {
        HttpClient client = await _factory.ClientWithTokenAsync(ApiScopes.HooksUptimeKuma);
        string body = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Webhooks", "Fixtures", "kuma-down.form.txt"));
        var content = new StringContent(body, Encoding.UTF8);
        content.Headers.ContentType = MediaTypeHeaderValue.Parse($"multipart/form-data; boundary={body[2..body.IndexOf('\r', StringComparison.Ordinal)]}");

        HttpResponseMessage response = await client.PostAsync(Kuma, content, Ct);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("monitor.down", Assert.Single(_events.Events).Type);
    }

    [Fact]
    public async Task Post_UnknownSource_Returns404EvenWithAHookToken()
    {
        HttpClient client = await _factory.ClientWithTokenAsync(ApiScopes.HooksUptimeKuma);

        HttpResponseMessage response = await client.PostAsync("/api/v1/hooks/proxmox", Json("down"), Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(_events.Events);
    }

    [Fact]
    public async Task Post_WithoutTheSourceScope_Returns403()
    {
        HttpClient client = await _factory.ClientWithTokenAsync(ApiScopes.ConversationWrite);

        HttpResponseMessage response = await client.PostAsync(Kuma, Json("down"), Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(_events.Events);
    }

    [Fact]
    public async Task Post_WithoutOrWithARevokedToken_Returns401()
    {
        string token = await _factory.CreateTokenAsync(ApiScopes.HooksUptimeKuma);
        await RevokeAllAsync();
        HttpClient revoked = _factory.CreateClient();
        revoked.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        Assert.Equal(HttpStatusCode.Unauthorized, (await revoked.PostAsync(Kuma, Json("down"), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().PostAsync(Kuma, Json("down"), Ct)).StatusCode);
        Assert.Empty(_events.Events);
    }

    [Fact]
    public async Task Post_100KbBody_Returns413()
    {
        HttpClient client = await _factory.ClientWithTokenAsync(ApiScopes.HooksUptimeKuma);
        string padded = $$"""{"heartbeat":null,"monitor":null,"msg":"{{new string('x', 100 * 1024)}}"}""";

        HttpResponseMessage response = await client.PostAsync(Kuma, new StringContent(padded, Encoding.UTF8, "application/json"), Ct);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Empty(_events.Events);
    }

    [Fact]
    public async Task Post_InvalidJson_Returns400()
    {
        HttpClient client = await _factory.ClientWithTokenAsync(ApiScopes.HooksUptimeKuma);

        HttpResponseMessage response = await client.PostAsync(Kuma, Json("invalid"), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("webhook.invalid_body", JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Post_TestNotification_Returns202AndPublishesNothing()
    {
        HttpClient client = await _factory.ClientWithTokenAsync(ApiScopes.HooksUptimeKuma);

        HttpResponseMessage response = await client.PostAsync(Kuma, Json("test"), Ct);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Empty(_events.Events);
    }

    [Fact]
    public async Task Post_BeyondThePerTokenLimit_Returns429()
    {
        HttpClient client = await _factory.ClientWithTokenAsync(ApiScopes.HooksUptimeKuma);
        HttpClient other = await _factory.ClientWithTokenAsync(ApiScopes.HooksUptimeKuma);

        for (int i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsync(Kuma, Json("test"), Ct)).StatusCode);

        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostAsync(Kuma, Json("test"), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await other.PostAsync(Kuma, Json("test"), Ct)).StatusCode);
    }

    [Fact]
    public async Task OpenApi_ListsTheHookEndpoint()
    {
        string document = await _factory.CreateClient().GetStringAsync("/openapi/v1.json", Ct);

        Assert.Contains("/api/v1/hooks/{source}", document, StringComparison.Ordinal);
    }

    private static StringContent Json(string fixture) =>
        new(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Webhooks", "Fixtures", $"kuma-{fixture}.json")), Encoding.UTF8, "application/json");

    private async Task<OwnerId> OwnerAsync()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IInstanceOwner>().GetAsync(Ct);
    }

    private async Task RevokeAllAsync()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        IExecutionContextAccessor accessor = scope.ServiceProvider.GetRequiredService<IExecutionContextAccessor>();
        using (accessor.Use(new MarshalExecutionContext { OwnerId = await OwnerAsync(), Source = SessionSource.Api }))
        {
            IApiTokenService tokens = scope.ServiceProvider.GetRequiredService<IApiTokenService>();
            foreach (ApiTokenInfo token in await tokens.ListAsync(Ct))
                await tokens.RevokeAsync(token.Id, Ct);
        }
    }
}
