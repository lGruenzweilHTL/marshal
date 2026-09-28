using System.Net;
using System.Net.Http.Json;
using Marshal.Abstractions.Identity;
using Marshal.Api.Tests.Http;
using Marshal.Data.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Marshal.Api.Tests.Auth;

public sealed class LocalLoginTests : IAsyncLifetime
{
    private const string Password = Browser.Password;

    private MarshalApiFactory _factory = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _factory = await AuthFactory.CreateAsync(AuthFactory.Local());

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task Setup_UnclaimedOwner_IsAvailable()
    {
        HttpResponseMessage response = await Client().GetAsync("/api/v1/auth/local/setup", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Setup_ClaimsTheInstanceOwnerAsAdminAndSignsIn()
    {
        OwnerId owner = await InstanceOwnerAsync();

        HttpResponseMessage response = await SetupAsync(Client());

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), cookie => cookie.StartsWith("marshal_session=", StringComparison.Ordinal));
        using IServiceScope scope = _factory.Services.CreateScope();
        UserManager<User> users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        User user = Assert.Single(users.Users);
        Assert.Equal(owner, user.Id);
        Assert.Equal("lukas", user.UserName);
        Assert.Equal("Lukas G", user.DisplayName);
        Assert.True(await users.CheckPasswordAsync(user, Password));
        Assert.Equal(["admin"], await users.GetRolesAsync(user));
    }

    [Fact]
    public async Task Setup_AfterOwnerIsClaimed_Returns404()
    {
        await SetupAsync(Client());

        Assert.Equal(HttpStatusCode.NotFound, (await Client().GetAsync("/api/v1/auth/local/setup", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SetupAsync(Client(), userName: "mallory")).StatusCode);
    }

    [Fact]
    public async Task Setup_WeakPassword_Returns400AndClaimsNothing()
    {
        HttpResponseMessage response = await Client().PostAsJsonAsync("/api/v1/auth/local/setup", new { userName = "lukas", displayName = "Lukas", password = "123" }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Client().GetAsync("/api/v1/auth/local/setup", Ct)).StatusCode);
    }

    [Fact]
    public async Task Setup_WithoutLocalProvider_Returns404()
    {
        await using MarshalApiFactory oidcOnly = await AuthFactory.CreateAsync(AuthFactory.Oidc("authentik", "Authentik"));

        HttpResponseMessage response = await Browser.Create(oidcOnly).GetAsync("/api/v1/auth/local/setup", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PasswordLogin_ValidCredentials_SignsIn()
    {
        await SetupAsync(Client());

        HttpResponseMessage response = await Client().PostAsJsonAsync("/api/v1/auth/local/login", new { userName = "lukas", password = Password }, Ct);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), cookie => cookie.StartsWith("marshal_session=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PasswordLogin_WrongPassword_Returns401WithoutSession()
    {
        await SetupAsync(Client());

        HttpResponseMessage response = await Client().PostAsJsonAsync("/api/v1/auth/local/login", new { userName = "lukas", password = "wrong" }, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie") && response.Headers.GetValues("Set-Cookie").Any(cookie => cookie.StartsWith("marshal_session=", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Passkey_RegisteredWhileSignedIn_SignsInWithoutPassword()
    {
        HttpClient setupClient = Client();
        await SetupAsync(setupClient);
        using var authenticator = new FakeAuthenticator("https://localhost", "localhost");

        string creationOptions = await (await setupClient.PostAsync("/api/v1/auth/local/passkeys/creation-options", null, Ct)).Content.ReadAsStringAsync(Ct);
        HttpResponseMessage registered = await setupClient.PostAsJsonAsync("/api/v1/auth/local/passkeys", new { credentialJson = authenticator.Create(creationOptions), name = "phone" }, Ct);
        Assert.True(registered.StatusCode == HttpStatusCode.NoContent, await registered.Content.ReadAsStringAsync(Ct));

        HttpClient browser = Client();
        string requestOptions = await (await browser.PostAsJsonAsync("/api/v1/auth/local/passkey/options", new { }, Ct)).Content.ReadAsStringAsync(Ct);
        HttpResponseMessage login = await browser.PostAsJsonAsync("/api/v1/auth/local/passkey", new { credentialJson = authenticator.Get(requestOptions) }, Ct);

        Assert.True(login.StatusCode == HttpStatusCode.NoContent, await login.Content.ReadAsStringAsync(Ct));
        Assert.Contains(login.Headers.GetValues("Set-Cookie"), cookie => cookie.StartsWith("marshal_session=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PasskeyRegistration_Anonymous_Returns401()
    {
        HttpResponseMessage response = await Client().PostAsync("/api/v1/auth/local/passkeys/creation-options", null, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PasskeyLogin_UnknownCredential_Returns401()
    {
        await SetupAsync(Client());
        using var stranger = new FakeAuthenticator("https://localhost", "localhost");
        HttpClient browser = Client();
        string requestOptions = await (await browser.PostAsJsonAsync("/api/v1/auth/local/passkey/options", new { }, Ct)).Content.ReadAsStringAsync(Ct);

        HttpResponseMessage login = await browser.PostAsJsonAsync("/api/v1/auth/local/passkey", new { credentialJson = stranger.Get(requestOptions) }, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }

    private HttpClient Client() => Browser.Create(_factory);

    private static Task<HttpResponseMessage> SetupAsync(HttpClient client, string userName = "lukas") =>
        client.PostAsJsonAsync("/api/v1/auth/local/setup", new { userName, displayName = "Lukas G", password = Password }, Ct);

    private async Task<OwnerId> InstanceOwnerAsync()
    {
        _ = _factory.Server;
        return await _factory.Services.GetRequiredService<IInstanceOwner>().GetAsync(Ct);
    }
}
