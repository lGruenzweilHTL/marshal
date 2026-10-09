using System.Net;
using System.Net.Http.Json;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Tokens;
using Marshal.Api.Tests.Http;

namespace Marshal.Api.Tests.Auth;

public sealed class MeApiTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Me_CookieUser_ReturnsIdNameRolesAndScopes()
    {
        await using MarshalApiFactory factory = await AuthFactory.CreateAsync(AuthFactory.Local());
        (HttpClient browser, OwnerId user) = await Browser.SignInAsync(factory, "user");

        MeBody? me = await browser.GetFromJsonAsync<MeBody>("/api/v1/me", Ct);

        Assert.NotNull(me);
        Assert.Equal(user.ToString(), me.Id);
        Assert.StartsWith("user-", me.DisplayName, StringComparison.Ordinal);
        Assert.Equal(["user"], me.Roles);
        Assert.Equal(ApiScopes.All.Where(scope => scope != ApiScopes.Admin && !ApiScopes.IsHook(scope)).Order(StringComparer.Ordinal), me.Scopes);
    }

    [Fact]
    public async Task Me_Anonymous_Returns401()
    {
        await using MarshalApiFactory factory = await AuthFactory.CreateAsync(AuthFactory.Local());

        HttpResponseMessage response = await Browser.Create(factory).GetAsync("/api/v1/me", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_Token_ReturnsOwnerAndTokenScopes()
    {
        await using var factory = new MarshalApiFactory();
        HttpClient client = await factory.ClientWithTokenAsync(ApiScopes.ConversationWrite);

        MeBody? me = await client.GetFromJsonAsync<MeBody>("/api/v1/me", Ct);

        Assert.Equal("Lukas", me?.DisplayName);
        Assert.Empty(me!.Roles);
        Assert.Equal([ApiScopes.ConversationWrite], me.Scopes);
    }

    private sealed record MeBody(string Id, string DisplayName, List<string> Roles, List<string> Scopes);
}
