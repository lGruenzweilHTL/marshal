using System.Net;
using System.Net.Http.Json;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Secrets;
using Marshal.Abstractions.Tokens;
using Marshal.Api.Tests.Auth;
using Marshal.Api.Tests.Http;
using Marshal.Data.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Marshal.Api.Tests.Security;

public sealed class SecretApiTests : IAsyncLifetime
{
    private const string Value = "s3cr3t-value-123";

    private MarshalApiFactory _factory = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        AuthFactory.ProviderSettings local = AuthFactory.Local();
        Dictionary<string, string?> settings = local.ToSettings(0);
        settings["integrations:freshrss:apiKeySecret"] = "freshrss_api";
        _factory = new MarshalApiFactory(settings);
        await File.WriteAllTextAsync(Path.Combine(_factory.SecretsDirectory, "moonraker_voron"), "file-value", Ct);
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task SetOwnSecret_IsStoredButNoEndpointReturnsTheValue()
    {
        (HttpClient browser, OwnerId user) = await Browser.SignInAsync(_factory, "admin");

        HttpResponseMessage put = await browser.PutAsJsonAsync("/api/v1/me/secrets/ha_token", new { value = Value }, Ct);

        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
        string overview = await browser.GetStringAsync("/api/v1/me/secrets", Ct);
        Assert.Contains("ha_token", overview, StringComparison.Ordinal);
        Assert.DoesNotContain(Value, overview, StringComparison.Ordinal);
        Assert.DoesNotContain(Value, await browser.GetStringAsync("/api/v1/secrets/instance", Ct), StringComparison.Ordinal);
        using (_factory.Services.GetRequiredService<IExecutionContextAccessor>().Use(new MarshalExecutionContext { OwnerId = user, Source = SessionSource.Web }))
            Assert.Equal(Value, (await _factory.Services.GetRequiredService<ISecretStore>().GetForOwnerAsync(SecretName.Create("ha_token").Value, user, Ct)).Value);
    }

    [Fact]
    public async Task Overview_ShowsScopeOriginAndLastUpdate()
    {
        (HttpClient browser, _) = await Browser.SignInAsync(_factory, "admin");
        await browser.PutAsJsonAsync("/api/v1/me/secrets/ha_token", new { value = Value }, Ct);
        await browser.PutAsJsonAsync("/api/v1/secrets/instance/shared_key", new { value = Value }, Ct);

        Overview overview = (await browser.GetFromJsonAsync<Overview>("/api/v1/me/secrets", Ct))!;

        Item own = overview.Secrets.Single(item => item.Name == "ha_token");
        Assert.Equal(("user", "database", false), (own.Scope, own.Origin, own.ReadOnly));
        Assert.NotNull(own.UpdatedAt);
        Item instance = overview.Secrets.Single(item => item.Name == "shared_key");
        Assert.Equal(("instance", "database"), (instance.Scope, instance.Origin));
    }

    [Fact]
    public async Task EnvironmentSecrets_AreListedReadOnlyWithoutValue()
    {
        (HttpClient browser, _) = await Browser.SignInAsync(_factory, "user");

        string raw = await browser.GetStringAsync("/api/v1/me/secrets", Ct);

        Overview overview = System.Text.Json.JsonSerializer.Deserialize<Overview>(raw, System.Text.Json.JsonSerializerOptions.Web)!;
        Item file = overview.Secrets.Single(item => item.Name == "moonraker_voron");
        Assert.Equal(("instance", "environment", true), (file.Scope, file.Origin, file.ReadOnly));
        Assert.DoesNotContain("file-value", raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnresolvedReference_IsFlaggedWithWhereItIsReferenced()
    {
        (HttpClient browser, _) = await Browser.SignInAsync(_factory, "admin");

        Overview before = (await browser.GetFromJsonAsync<Overview>("/api/v1/me/secrets", Ct))!;
        await browser.PutAsJsonAsync("/api/v1/secrets/instance/freshrss_api", new { value = Value }, Ct);
        Overview after = (await browser.GetFromJsonAsync<Overview>("/api/v1/me/secrets", Ct))!;

        Unresolved unresolved = Assert.Single(before.Unresolved);
        Assert.Equal("freshrss_api", unresolved.Name);
        Assert.Equal(["marshal.yaml: integrations.freshrss.apiKeySecret"], unresolved.ReferencedBy);
        Assert.Empty(after.Unresolved);
    }

    [Fact]
    public async Task InstanceSecret_WithoutAdmin_Returns403()
    {
        (HttpClient browser, _) = await Browser.SignInAsync(_factory, "user");

        HttpResponseMessage put = await browser.PutAsJsonAsync("/api/v1/secrets/instance/shared_key", new { value = Value }, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, put.StatusCode);
        Assert.True((await _factory.Services.GetRequiredService<ISecretStore>().GetAsync(SecretName.Create("shared_key").Value, Ct)).IsFailure);
    }

    [Fact]
    public async Task DeleteOwnSecret_RemovesIt()
    {
        (HttpClient browser, _) = await Browser.SignInAsync(_factory, "user");
        await browser.PutAsJsonAsync("/api/v1/me/secrets/ha_token", new { value = Value }, Ct);

        HttpResponseMessage delete = await browser.DeleteAsync("/api/v1/me/secrets/ha_token", Ct);

        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.DoesNotContain("ha_token", await browser.GetStringAsync("/api/v1/me/secrets", Ct), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, (await browser.DeleteAsync("/api/v1/me/secrets/ha_token", Ct)).StatusCode);
    }

    [Fact]
    public async Task Changes_AreAuditedWithoutTheValue()
    {
        (HttpClient browser, OwnerId user) = await Browser.SignInAsync(_factory, "user");

        await browser.PutAsJsonAsync("/api/v1/me/secrets/ha_token", new { value = Value }, Ct);
        await browser.DeleteAsync("/api/v1/me/secrets/ha_token", Ct);

        List<AuditEvent> audit = await _factory.WithDatabaseAsync(user, db => db.AuditEvents.Where(entry => entry.Action.StartsWith("secret.")).OrderBy(entry => entry.CreatedAt).ToListAsync(Ct));
        Assert.Equal(["secret.set", "secret.delete"], audit.Select(entry => entry.Action));
        Assert.All(audit, entry => Assert.Equal("ha_token", entry.Target));
        Assert.All(audit, entry => Assert.DoesNotContain(Value, entry.Details ?? "", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Bad-Name")]
    [InlineData("1abc")]
    public async Task InvalidName_Returns400(string name)
    {
        (HttpClient browser, _) = await Browser.SignInAsync(_factory, "user");

        HttpResponseMessage put = await browser.PutAsJsonAsync($"/api/v1/me/secrets/{name}", new { value = Value }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
    }

    [Fact]
    public async Task TokenPrincipal_CannotManageSecrets()
    {
        HttpClient machine = await _factory.ClientWithTokenAsync(ApiScopes.Admin);

        Assert.Equal(HttpStatusCode.Forbidden, (await machine.GetAsync("/api/v1/me/secrets", Ct)).StatusCode);
    }

    private sealed record Overview(List<Item> Secrets, List<Unresolved> Unresolved);

    private sealed record Item(string Name, string Scope, string Origin, DateTimeOffset? UpdatedAt, bool ReadOnly);

    private sealed record Unresolved(string Name, List<string> ReferencedBy);
}
