using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Marshal.Abstractions.Events;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Tokens;
using Marshal.Api.Auth;
using Marshal.Api.Tests.Http;
using Marshal.Data.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace Marshal.Api.Tests.Auth;

public sealed class AuthorizationTests : IAsyncLifetime
{
    private MarshalApiFactory _factory = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _factory = await AuthFactory.CreateAsync(AuthFactory.Local());
        _ = _factory.Server;
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task CookiePrincipal_UserRole_GetsEveryScopeExceptAdminAndHooks()
    {
        ClaimsPrincipal principal = await TransformAsync(CookiePrincipal("user"));

        Assert.Equal(ApiScopes.All.Where(scope => scope != ApiScopes.Admin && !ApiScopes.IsHook(scope)).Order(StringComparer.Ordinal), Scopes(principal));
        Assert.DoesNotContain(ApiScopes.HooksUptimeKuma, Scopes(principal));
    }

    [Fact]
    public async Task CookiePrincipal_AdminRole_GetsAllScopes()
    {
        ClaimsPrincipal principal = await TransformAsync(CookiePrincipal("admin"));

        Assert.Equal(ApiScopes.All.Order(StringComparer.Ordinal), Scopes(principal));
    }

    [Fact]
    public void HookScopes_ExistForEveryWebhookSource()
    {
        IWebhookIngest ingest = _factory.Services.GetRequiredService<IWebhookIngest>();

        Assert.Equal(["proxmox", "seerr", "uptime-kuma"], ingest.Sources.Order(StringComparer.Ordinal));
        Assert.All(ingest.Sources, source => Assert.Contains(ApiScopes.Hooks(source), ApiScopes.All));
        Assert.Equal(("hooks:uptime-kuma", "hooks:proxmox", "hooks:seerr"), (ApiScopes.HooksUptimeKuma, ApiScopes.HooksProxmox, ApiScopes.HooksSeerr));
    }

    [Fact]
    public async Task Transformation_RunTwice_DoesNotDuplicateScopes()
    {
        ClaimsPrincipal principal = await TransformAsync(await TransformAsync(CookiePrincipal("user")));

        Assert.Equal(Scopes(principal).Distinct(), Scopes(principal));
    }

    [Fact]
    public async Task TokenPrincipal_KeepsItsOwnScopes()
    {
        var token = new ClaimsPrincipal(new ClaimsIdentity([new Claim(MarshalClaims.Scope, ApiScopes.ConversationWrite), new Claim(ClaimTypes.Role, "admin")], MarshalAuthSchemes.ApiToken));

        ClaimsPrincipal principal = await TransformAsync(token);

        Assert.Equal([ApiScopes.ConversationWrite], Scopes(principal));
    }

    [Theory]
    [InlineData("user", ApiScopes.Admin, false)]
    [InlineData("user", ApiScopes.ConversationWrite, true)]
    [InlineData("admin", ApiScopes.Admin, true)]
    [InlineData("admin", ApiScopes.HooksUptimeKuma, true)]
    [InlineData("user", ApiScopes.HooksUptimeKuma, false)]
    [InlineData("admin", ApiScopes.HooksProxmox, true)]
    [InlineData("user", ApiScopes.HooksProxmox, false)]
    [InlineData("admin", ApiScopes.HooksSeerr, true)]
    [InlineData("user", ApiScopes.HooksSeerr, false)]
    public async Task CookieAndTokenPrincipals_GoThroughTheSameScopePolicies(string role, string policy, bool allowed)
    {
        IAuthorizationService authorization = _factory.Services.GetRequiredService<IAuthorizationService>();
        ClaimsPrincipal cookie = await TransformAsync(CookiePrincipal(role));
        var token = new ClaimsPrincipal(new ClaimsIdentity(
            Roles.ScopesOf([role]).Select(scope => new Claim(MarshalClaims.Scope, scope)), MarshalAuthSchemes.ApiToken));

        Assert.Equal(allowed, (await authorization.AuthorizeAsync(cookie, policy)).Succeeded);
        Assert.Equal(allowed, (await authorization.AuthorizeAsync(token, policy)).Succeeded);
    }

    [Fact]
    public async Task CookiePost_WithoutAntiforgeryToken_Returns400AndChangesNothing()
    {
        (HttpClient signedIn, _) = await Browser.SignInAsync(_factory, "user");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout");
        request.Headers.Add(Browser.SkipAntiforgery, "1");

        HttpResponseMessage response = await signedIn.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await signedIn.GetAsync("/api/v1/me", Ct)).StatusCode);
    }

    [Fact]
    public async Task CookiePost_WithAntiforgeryToken_IsAccepted()
    {
        (HttpClient signedIn, _) = await Browser.SignInAsync(_factory, "user");

        HttpResponseMessage response = await signedIn.PostAsync("/api/v1/auth/logout", null, Ct);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await signedIn.GetAsync("/api/v1/me", Ct)).StatusCode);
    }

    [Fact]
    public async Task LogoutFormPost_WithTokenInFormField_IsAccepted()
    {
        (HttpClient signedIn, _) = await Browser.SignInAsync(_factory, "user");
        string token = (await signedIn.GetFromJsonAsync<JsonElement>("/api/v1/auth/antiforgery", Ct)).GetProperty("token").GetString()!;
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout")
        {
            Content = new FormUrlEncodedContent([new("__RequestVerificationToken", token)]),
        };
        request.Headers.Add(Browser.SkipAntiforgery, "1");

        HttpResponseMessage response = await signedIn.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await signedIn.GetAsync("/api/v1/me", Ct)).StatusCode);
    }

    [Fact]
    public async Task AnonymousPost_WithoutAntiforgeryToken_Returns400()
    {
        HttpResponseMessage response = await Browser.Create(_factory, antiforgery: false)
            .PostAsJsonAsync("/api/v1/auth/local/login", new { userName = "x", password = "y" }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task BrowserPost_OverPlainHttp_Returns400HttpsRequired()
    {
        HttpResponseMessage response = await _factory.CreateClient().PostAsJsonAsync("/api/v1/auth/local/login", new { userName = "x", password = "y" }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("auth.https_required", await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AntiforgeryToken_OverPlainHttp_Returns400HttpsRequired()
    {
        HttpResponseMessage response = await _factory.CreateClient().GetAsync("/api/v1/auth/antiforgery", Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("auth.https_required", (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task BrowserPost_ForwardedAsHttpsByTheProxy_PassesTheHttpsCheck()
    {
        HttpClient client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/auth/local/login", new { userName = "x", password = "y" }, Ct);

        Assert.Contains("auth.antiforgery_invalid", await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task BearerPost_NeedsNoAntiforgeryToken()
    {
        HttpClient client = await _factory.ClientWithTokenAsync(ApiScopes.ConversationWrite);

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/conversation/turns", new { text = "" }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("conversation.text_required", await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    private static ClaimsPrincipal CookiePrincipal(string role) =>
        UserAccounts.CreatePrincipal(new User { Id = OwnerId.New(), UserName = "u", DisplayName = "U", CreatedAt = DateTimeOffset.UnixEpoch }, [role], "local");

    private Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal) =>
        _factory.Services.GetRequiredService<IClaimsTransformation>().TransformAsync(principal);

    private static List<string> Scopes(ClaimsPrincipal principal) => [.. principal.FindAll(MarshalClaims.Scope).Select(claim => claim.Value)];
}
