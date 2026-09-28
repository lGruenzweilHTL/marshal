using System.Net;
using System.Net.Http.Json;
using Marshal.Api.Tests.Http;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Marshal.Api.Tests.Auth;

public sealed class LoginProviderApiTests
{
    [Fact]
    public async Task Providers_OidcAndLocal_ListsBothWithoutAuthentication()
    {
        using MarshalApiFactory factory = await AuthFactory.CreateAsync(AuthFactory.Oidc("authentik", "Authentik"), AuthFactory.Local());

        List<ProviderBody>? providers = await factory.CreateClient().GetFromJsonAsync<List<ProviderBody>>("/api/v1/auth/providers", Ct);

        Assert.Equal([new ProviderBody("authentik", "oidc", "Authentik"), new ProviderBody("local", "local", "Local account")], providers);
    }

    [Fact]
    public async Task Startup_OidcClientSecretMissing_FailsNamingReference()
    {
        using var factory = new MarshalApiFactory(AuthFactory.Oidc("authentik", "Authentik").ToSettings(0));

        Exception error = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("auth.providers[0].clientCredentialSecret (authentik_client)", StartupErrors.Flatten(error), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Startup_OidcClientSecretFromSecretStore_ConfiguresHandler()
    {
        using MarshalApiFactory factory = await AuthFactory.CreateAsync(AuthFactory.Oidc("authentik", "Authentik"));
        factory.CreateClient();

        OpenIdConnectOptions options = factory.Services.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>().Get("authentik");

        Assert.Equal(AuthFactory.ClientSecret, options.ClientSecret);
        Assert.Equal("marshal", options.ClientId);
        Assert.True(options.UsePkce);
        Assert.Equal("code", options.ResponseType);
        Assert.Equal("/signin-oidc/authentik", options.CallbackPath);
    }

    [Theory]
    [InlineData("auth:providers:0:type", "saml", "auth.providers[0].type")]
    [InlineData("auth:providers:0:name", "Bad Name", "auth.providers[0].name")]
    [InlineData("auth:providers:0:authority", "", "auth.providers[0].authority")]
    [InlineData("auth:providers:0:roleMappings:marshal-admins", "root", "auth.providers[0].roleMappings")]
    public async Task Startup_InvalidProvider_FailsNamingKey(string key, string value, string expected)
    {
        Dictionary<string, string?> settings = AuthFactory.Oidc("authentik", "Authentik").ToSettings(0);
        settings[key] = value;
        using var factory = new MarshalApiFactory(settings);
        await AuthFactory.WriteClientSecretAsync(factory);

        Exception error = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains(expected, StartupErrors.Flatten(error), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Startup_DuplicateProviderNames_Fails()
    {
        using MarshalApiFactory factory = await AuthFactory.CreateAsync(AuthFactory.Local(), AuthFactory.Local());

        Exception error = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("auth.providers", StartupErrors.Flatten(error), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Login_UnknownProvider_Returns404()
    {
        using MarshalApiFactory factory = await AuthFactory.CreateAsync(AuthFactory.Local());

        HttpResponseMessage response = await NoRedirects(factory).GetAsync("/api/v1/auth/login/nope", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Login_Local_RedirectsToTheSpaLoginPage()
    {
        using MarshalApiFactory factory = await AuthFactory.CreateAsync(AuthFactory.Local());

        HttpResponseMessage response = await NoRedirects(factory).GetAsync("/api/v1/auth/login/local?returnUrl=%2Fruns%2F1", Ct);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/login/local?returnUrl=%2Fruns%2F1", response.Headers.Location?.OriginalString);
    }

    [Theory]
    [InlineData("https://evil.example/")]
    [InlineData("//evil.example/")]
    [InlineData("/\\evil.example/")]
    public async Task Login_ExternalReturnUrl_Returns400(string returnUrl)
    {
        using MarshalApiFactory factory = await AuthFactory.CreateAsync(AuthFactory.Local());

        HttpResponseMessage response = await NoRedirects(factory).GetAsync($"/api/v1/auth/login/local?returnUrl={Uri.EscapeDataString(returnUrl)}", Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static HttpClient NoRedirects(MarshalApiFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record ProviderBody(string Name, string Type, string DisplayName);
}
