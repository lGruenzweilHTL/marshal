using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Tokens;
using Marshal.Api.Tests.Auth;
using Marshal.Api.Tests.Http;
using Marshal.Data.Audit;
using Marshal.Data.Tokens;
using Microsoft.EntityFrameworkCore;

namespace Marshal.Api.Tests.Security;

public sealed class TokenApiTests : IAsyncLifetime
{
    private const string Tokens = "/api/v1/me/tokens";

    private MarshalApiFactory _factory = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _factory = await AuthFactory.CreateAsync(AuthFactory.Local());

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task Create_ReturnsPlaintextOnceAndTheTokenWorks()
    {
        (HttpClient browser, OwnerId user) = await Browser.SignInAsync(_factory, "user");

        HttpResponseMessage response = await browser.PostAsJsonAsync(Tokens, new { name = "Home Assistant", scopes = new[] { ApiScopes.ConversationWrite } }, Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        CreatedBody created = (await response.Content.ReadFromJsonAsync<CreatedBody>(Ct))!;
        Assert.StartsWith("mrs_", created.Token, StringComparison.Ordinal);
        string list = await browser.GetStringAsync(Tokens, Ct);
        Assert.Contains("Home Assistant", list, StringComparison.Ordinal);
        Assert.DoesNotContain(created.Token, list, StringComparison.Ordinal);
        using HttpClient machine = _factory.CreateClient();
        machine.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", created.Token);
        Assert.Equal(user.ToString(), (await machine.GetFromJsonAsync<MeBody>("/api/v1/me", Ct))?.Id);
    }

    [Fact]
    public async Task Create_ScopeTheUserDoesNotHold_IsRejected()
    {
        (HttpClient browser, OwnerId user) = await Browser.SignInAsync(_factory, "user");

        HttpResponseMessage response = await browser.PostAsJsonAsync(Tokens, new { name = "sneaky", scopes = new[] { ApiScopes.Admin } }, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, await _factory.WithDatabaseAsync(user, db => db.ApiTokens.CountAsync(Ct)));
    }

    [Fact]
    public async Task Create_AdminMayGrantAdmin()
    {
        (HttpClient browser, _) = await Browser.SignInAsync(_factory, "admin");

        HttpResponseMessage response = await browser.PostAsJsonAsync(Tokens, new { name = "ops", scopes = new[] { ApiScopes.Admin } }, Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Create_ExecutorCallbackKind_IsRejected()
    {
        (HttpClient browser, _) = await Browser.SignInAsync(_factory, "admin");

        HttpResponseMessage response = await browser.PostAsJsonAsync(Tokens, new { name = "cb", scopes = new[] { ApiScopes.TasksWrite }, kind = "executorCallback" }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_IsAuditedWithTheUserAsActor()
    {
        (HttpClient browser, OwnerId user) = await Browser.SignInAsync(_factory, "user");

        await browser.PostAsJsonAsync(Tokens, new { name = "ha", scopes = new[] { ApiScopes.ConversationWrite } }, Ct);

        AuditEvent audit = await _factory.WithDatabaseAsync(user, db => db.AuditEvents.SingleAsync(entry => entry.Action == "token.create", Ct));
        Assert.Equal(Abstractions.Audit.AuditActorKind.User, audit.ActorKind);
        Assert.Equal(user.ToString(), audit.ActorId);
        Assert.DoesNotContain("mrs_", audit.Details ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public async Task Revoke_NextRequestWithTheToken_Returns401AndIsAudited()
    {
        (HttpClient browser, OwnerId user) = await Browser.SignInAsync(_factory, "user");
        CreatedBody created = (await (await browser.PostAsJsonAsync(Tokens, new { name = "ha", scopes = new[] { ApiScopes.ConversationWrite } }, Ct)).Content.ReadFromJsonAsync<CreatedBody>(Ct))!;

        HttpResponseMessage revoke = await browser.DeleteAsync($"{Tokens}/{created.Info.Id}", Ct);

        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);
        using HttpClient machine = _factory.CreateClient();
        machine.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", created.Token);
        Assert.Equal(HttpStatusCode.Unauthorized, (await machine.GetAsync("/api/v1/me", Ct)).StatusCode);
        Assert.Equal(1, await _factory.WithDatabaseAsync(user, db => db.AuditEvents.CountAsync(entry => entry.Action == "token.revoke" && entry.Target == created.Info.Id, Ct)));
    }

    [Fact]
    public async Task Revoke_AnotherUsersToken_Returns404()
    {
        (HttpClient alice, _) = await Browser.SignInAsync(_factory, "user");
        (HttpClient bob, _) = await Browser.SignInAsync(_factory, "user");
        CreatedBody created = (await (await alice.PostAsJsonAsync(Tokens, new { name = "ha", scopes = new[] { ApiScopes.ConversationWrite } }, Ct)).Content.ReadFromJsonAsync<CreatedBody>(Ct))!;

        HttpResponseMessage revoke = await bob.DeleteAsync($"{Tokens}/{created.Info.Id}", Ct);

        Assert.Equal(HttpStatusCode.NotFound, revoke.StatusCode);
        Assert.DoesNotContain(created.Info.Id, await bob.GetStringAsync(Tokens, Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecutorCallbackToken_IsListedReadOnlyAndCannotBeRevoked()
    {
        (HttpClient browser, OwnerId user) = await Browser.SignInAsync(_factory, "user");
        var id = ApiTokenId.New();
        await _factory.WithDatabaseAsync(user, async db =>
        {
            db.ApiTokens.Add(new ApiToken { Id = id, OwnerId = user, Name = "task 7", TokenHash = "hash", Scopes = [ApiScopes.TasksWrite], Kind = ApiTokenKind.ExecutorCallback, CreatedAt = DateTimeOffset.UtcNow });
            return await db.SaveChangesAsync(Ct);
        });

        List<InfoBody>? list = await browser.GetFromJsonAsync<List<InfoBody>>(Tokens, Ct);
        HttpResponseMessage revoke = await browser.DeleteAsync($"{Tokens}/{id}", Ct);

        InfoBody callback = Assert.Single(list!);
        Assert.True(callback.ReadOnly);
        Assert.Equal("executorCallback", callback.Kind);
        Assert.Equal(HttpStatusCode.BadRequest, revoke.StatusCode);
    }

    [Fact]
    public async Task TokenPrincipal_CannotManageTokens()
    {
        HttpClient machine = await _factory.ClientWithTokenAsync(ApiScopes.Admin);

        HttpResponseMessage response = await machine.GetAsync(Tokens, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private sealed record MeBody(string Id);

    private sealed record InfoBody(string Id, string Name, List<string> Scopes, string Kind, bool ReadOnly);

    private sealed record CreatedBody(InfoBody Info, string Token);
}
