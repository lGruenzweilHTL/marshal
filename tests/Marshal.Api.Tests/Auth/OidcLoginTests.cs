using System.Net;
using Marshal.Abstractions.Identity;
using Marshal.Api.Tests.Http;
using Marshal.Data.Identity;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;

namespace Marshal.Api.Tests.Auth;

public sealed class OidcLoginTests : IAsyncLifetime
{
    private readonly FakeIdentityProvider _idp = new();
    private MarshalApiFactory _factory = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _factory = await AuthFactory.CreateAsync(AuthFactory.Oidc("authentik", "Authentik"));
        _factory.TestServices = services => services.ConfigureAll<OpenIdConnectOptions>(options => options.BackchannelHttpHandler = _idp);
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        _idp.Dispose();
    }

    [Fact]
    public async Task Login_StartsCodeFlowWithPkce()
    {
        HttpResponseMessage start = await Client().GetAsync("/api/v1/auth/login/authentik?returnUrl=%2Fchat", Ct);

        Assert.Equal(HttpStatusCode.Redirect, start.StatusCode);
        Uri authorize = start.Headers.Location!;
        Dictionary<string, StringValues> query = QueryHelpers.ParseQuery(authorize.Query);
        Assert.Equal("https://idp.test/authorize", authorize.GetLeftPart(UriPartial.Path));
        Assert.Equal("code", query["response_type"]);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.Equal("https://localhost/signin-oidc/authentik", query["redirect_uri"]);
    }

    [Fact]
    public async Task Login_Completes_SetsSessionCookieAndNoIdpTokenReachesTheBrowser()
    {
        HttpResponseMessage callback = await LoginAsync(Client(), "sub-1", ["marshal-admins"]);

        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Equal("/chat", callback.Headers.Location?.OriginalString);
        string session = Assert.Single(callback.Headers.GetValues("Set-Cookie"), cookie => cookie.StartsWith("marshal_session=", StringComparison.Ordinal));
        Assert.Contains("httponly", session, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", session, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", session, StringComparison.OrdinalIgnoreCase);
        string everythingSent = string.Join("\n", callback.Headers.SelectMany(header => header.Value)) + await callback.Content.ReadAsStringAsync(Ct);
        Assert.NotEmpty(_idp.IssuedTokens);
        Assert.All(_idp.IssuedTokens, token => Assert.DoesNotContain(token, everythingSent, StringComparison.Ordinal));
        Assert.True(session.Length < 1000, "the cookie holds only a ticket key");
    }

    [Fact]
    public async Task Login_FirstSubject_ClaimsTheInstanceOwner()
    {
        OwnerId owner = await InstanceOwnerAsync();

        await LoginAsync(Client(), "sub-1", ["marshal-admins"]);

        User? user = await FindByLoginAsync("sub-1");
        Assert.Equal(owner, user?.Id);
    }

    [Fact]
    public async Task Login_EmailChangedAtIdp_MapsToSameUserBySubject()
    {
        await LoginAsync(Client(), "sub-1", ["marshal-users"], email: "old@example.com");
        OwnerId first = (await FindByLoginAsync("sub-1"))!.Id;

        await LoginAsync(Client(), "sub-1", ["marshal-users"], email: "new@example.com");

        Assert.Equal(first, (await FindByLoginAsync("sub-1"))!.Id);
        Assert.Single(Users());
    }

    [Fact]
    public async Task Login_SubjectAfterOwnerIsClaimed_CreatesNewUser()
    {
        await LoginAsync(Client(), "sub-1", ["marshal-admins"]);

        await LoginAsync(Client(), "sub-2", ["marshal-users"], name: "Anna");

        User? second = await FindByLoginAsync("sub-2");
        Assert.NotNull(second);
        Assert.NotEqual(await InstanceOwnerAsync(), second.Id);
        Assert.Equal("Anna", second.DisplayName);
        Assert.Equal(2, Users().Count);
        UserPreferences preferences = await _factory.WithDatabaseAsync(second.Id, db => db.UserPreferences.SingleAsync(row => row.UserId == second.Id, Ct));
        Assert.Equal("Europe/Vienna", preferences.TimeZone);
    }

    [Fact]
    public async Task Login_WithoutMappedGroup_IsDeniedAndCreatesNoUser()
    {
        HttpResponseMessage callback = await LoginAsync(Client(), "sub-1", ["other-group"]);

        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Equal("/login?error=access_denied", callback.Headers.Location?.OriginalString);
        Assert.DoesNotContain(callback.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? cookies) ? cookies : [], cookie => cookie.StartsWith("marshal_session=", StringComparison.Ordinal));
        Assert.Null(await FindByLoginAsync("sub-1"));
        Assert.Single(Users());
    }

    [Fact]
    public async Task Login_GroupMappings_SetRoles()
    {
        await LoginAsync(Client(), "sub-1", ["marshal-admins", "marshal-users"]);

        Assert.Equal(["admin", "user"], await RolesAsync("sub-1"));
    }

    [Fact]
    public async Task Login_GroupRemovedAtIdp_RemovesRoleOnNextLogin()
    {
        await LoginAsync(Client(), "sub-1", ["marshal-admins"]);

        await LoginAsync(Client(), "sub-1", ["marshal-users"]);

        Assert.Equal(["user"], await RolesAsync("sub-1"));
    }

    private HttpClient Client() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });

    private async Task<HttpResponseMessage> LoginAsync(HttpClient client, string subject, string[] groups, string email = "user@example.com", string name = "Lukas")
    {
        HttpResponseMessage start = await client.GetAsync("/api/v1/auth/login/authentik?returnUrl=%2Fchat", Ct);
        Dictionary<string, StringValues> query = QueryHelpers.ParseQuery(start.Headers.Location!.Query);
        string code = Guid.NewGuid().ToString("N");
        _idp.Grant(code, query["code_challenge"]!, query["nonce"]!, new Dictionary<string, object>
        {
            ["sub"] = subject,
            ["name"] = name,
            ["email"] = email,
            ["groups"] = groups,
        });

        return await client.PostAsync("/signin-oidc/authentik", new FormUrlEncodedContent(new Dictionary<string, string> { ["code"] = code, ["state"] = query["state"]! }), Ct);
    }

    private async Task<OwnerId> InstanceOwnerAsync()
    {
        _ = _factory.Server;
        return await _factory.Services.GetRequiredService<IInstanceOwner>().GetAsync(Ct);
    }

    private async Task<User?> FindByLoginAsync(string subject)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<UserManager<User>>().FindByLoginAsync("authentik", subject);
    }

    private List<User> Users()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        return [.. scope.ServiceProvider.GetRequiredService<UserManager<User>>().Users];
    }

    private async Task<List<string>> RolesAsync(string subject)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        UserManager<User> users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        User user = (await users.FindByLoginAsync("authentik", subject))!;
        return [.. (await users.GetRolesAsync(user)).Order(StringComparer.Ordinal)];
    }
}
