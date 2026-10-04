using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Identity;
using Marshal.Api.Tests.Auth;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Marshal.Api.Tests.Http;

public sealed class UnexpectedErrorApiTests : IAsyncLifetime
{
    private const string SecretPath = "/data/internal/marshal.db";

    private MarshalApiFactory _factory = null!;

    private ThrowingProfiles? _profiles;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _factory = await AuthFactory.CreateAsync(AuthFactory.Local());
        // `/me` reads the profile; once armed, the provider throws and `/me` becomes an endpoint with an unhandled
        // exception. Startup (system task seeding) reads profiles too, so the provider throws only after it is armed.
        _factory.TestServices = services =>
        {
            ServiceDescriptor real = services.Single(service => service.ServiceType == typeof(IUserProfileProvider));
            services.RemoveAll<IUserProfileProvider>();
            services.AddSingleton<IUserProfileProvider>(provider =>
                _profiles = new ThrowingProfiles((IUserProfileProvider)ActivatorUtilities.CreateInstance(provider, real.ImplementationType!)));
        };
        _ = _factory.Server;
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task UnhandledException_Returns500ProblemWithCodeAndTraceId()
    {
        (HttpClient client, _) = await Browser.SignInAsync(_factory, "user");
        _profiles!.Armed = true;

        HttpResponseMessage response = await client.GetAsync("/api/v1/me", Ct);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("server.unexpected", body.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task UnhandledException_BodyDoesNotContainTheExceptionMessage()
    {
        (HttpClient client, _) = await Browser.SignInAsync(_factory, "user");
        _profiles!.Armed = true;

        string body = await (await client.GetAsync("/api/v1/me", Ct)).Content.ReadAsStringAsync(Ct);

        Assert.DoesNotContain(SecretPath, body, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(InvalidOperationException), body, StringComparison.Ordinal);
    }

    private sealed class ThrowingProfiles(IUserProfileProvider inner) : IUserProfileProvider
    {
        public bool Armed { get; set; }

        public Task<Result<UserProfile>> GetAsync(OwnerId owner, CancellationToken cancellationToken) =>
            Armed ? throw new InvalidOperationException($"Could not open {SecretPath}.") : inner.GetAsync(owner, cancellationToken);
    }
}
