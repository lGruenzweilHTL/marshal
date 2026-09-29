using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Secrets;
using Marshal.Api.Tests.Auth;
using Marshal.Data.Audit;
using Marshal.Integrations.Mail.Credentials;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;

namespace Marshal.Api.Tests.Http;

public sealed class MailAccountsApiTests : IAsyncLifetime
{
    private const string Accounts = "/api/v1/mail/accounts";

    private readonly TokenEndpoint _tokens = new();
    private MarshalApiFactory _factory = null!;
    private HttpClient _browser = null!;
    private OwnerId _user;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        Dictionary<string, string?> settings = AuthFactory.Local().ToSettings(0);
        settings["oauth:google:clientId"] = "google-client-id";
        settings["oauth:google:clientCredentialSecret"] = "google_client";
        _factory = new MarshalApiFactory(settings)
        {
            TestServices = services => services.AddHttpClient(OAuthTokenClient.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => _tokens),
        };
        await File.WriteAllTextAsync(Path.Combine(_factory.SecretsDirectory, "google_client"), "google-client-secret", Ct);
        (_browser, _user) = await Browser.SignInAsync(_factory, "user");
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task CreateImapAccount_PasswordBecomesAWriteOnlyOwnerSecret()
    {
        HttpResponseMessage created = await _browser.PostAsJsonAsync(Accounts, new
        {
            alias = "private",
            host = "imap.example.com",
            port = 993,
            security = "tls",
            userName = "lukas@example.com",
            password = "app-password",
            draftsFolder = "Entwürfe",
        }, Ct);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        string body = await created.Content.ReadAsStringAsync(Ct);
        Assert.DoesNotContain("app-password", body, StringComparison.Ordinal);
        JsonElement account = Assert.Single((await _browser.GetFromJsonAsync<List<JsonElement>>(Accounts, Ct))!);
        Assert.Equal(("private", "password", "imap.example.com", "Entwürfe"), (
            account.GetProperty("alias").GetString(),
            account.GetProperty("strategy").GetString(),
            account.GetProperty("host").GetString(),
            account.GetProperty("draftsFolder").GetString()));
        Assert.Equal("app-password", await OwnerSecretAsync("mail_private_password"));
        AuditEvent audit = Assert.Single(await AuditAsync("mail.account.create"));
        Assert.Equal(_user.Value.ToString(), audit.ActorId);
    }

    [Fact]
    public async Task CreateImapAccount_AliasTakenOrInvalid()
    {
        object account = new { alias = "private", host = "imap.example.com", port = 993, security = "tls", userName = "lukas@example.com", password = "x" };
        await _browser.PostAsJsonAsync(Accounts, account, Ct);

        HttpResponseMessage taken = await _browser.PostAsJsonAsync(Accounts, account, Ct);
        HttpResponseMessage invalid = await _browser.PostAsJsonAsync(Accounts, new { alias = "My Mail", host = "imap.example.com", port = 993, security = "tls", userName = "a@b.c", password = "x" }, Ct);

        Assert.Equal(HttpStatusCode.Conflict, taken.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task Ownership_OtherUsersSeeAndDeleteNothing()
    {
        await _browser.PostAsJsonAsync(Accounts, new { alias = "private", host = "imap.example.com", port = 993, security = "tls", userName = "lukas@example.com", password = "x" }, Ct);
        string id = (await _browser.GetFromJsonAsync<List<JsonElement>>(Accounts, Ct))![0].GetProperty("id").GetString()!;
        (HttpClient other, _) = await Browser.SignInAsync(_factory, "user");

        Assert.Empty((await other.GetFromJsonAsync<List<JsonElement>>(Accounts, Ct))!);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"{Accounts}/{id}", Ct)).StatusCode);
        Assert.Single((await _browser.GetFromJsonAsync<List<JsonElement>>(Accounts, Ct))!);
    }

    [Fact]
    public async Task Delete_RemovesAccountAndItsSecret()
    {
        await _browser.PostAsJsonAsync(Accounts, new { alias = "private", host = "imap.example.com", port = 993, security = "tls", userName = "lukas@example.com", password = "x" }, Ct);
        string id = (await _browser.GetFromJsonAsync<List<JsonElement>>(Accounts, Ct))![0].GetProperty("id").GetString()!;

        HttpResponseMessage deleted = await _browser.DeleteAsync($"{Accounts}/{id}", Ct);

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Empty((await _browser.GetFromJsonAsync<List<JsonElement>>(Accounts, Ct))!);
        Assert.Null(await OwnerSecretAsync("mail_private_password"));
        Assert.Single(await AuditAsync("mail.account.delete"));
    }

    [Fact]
    public async Task OAuthProviders_ShowWhichAreConfigured()
    {
        List<JsonElement> providers = (await _browser.GetFromJsonAsync<List<JsonElement>>("/api/v1/mail/oauth/providers", Ct))!;

        Assert.Equal([("google", true), ("microsoft", false)], providers.Select(provider => (provider.GetProperty("name").GetString(), provider.GetProperty("configured").GetBoolean())));
    }

    [Fact]
    public async Task OAuthConnect_CreatesTheAccountWithTheRefreshTokenAsOwnerSecret()
    {
        _tokens.Response = """{"access_token":"ya29","expires_in":3599,"refresh_token":"refresh-1","token_type":"Bearer"}""";

        Uri authorize = await StartAsync("google", "gmail");
        Dictionary<string, StringValues> query = QueryHelpers.ParseQuery(authorize.Query);
        HttpResponseMessage callback = await _browser.GetAsync($"/oauth/callback/google?code=code-1&state={Uri.EscapeDataString(query["state"]!)}", Ct);

        Assert.Equal("accounts.google.com", authorize.Host);
        Assert.Equal($"{Browser.Origin}/oauth/callback/google", query["redirect_uri"].ToString());
        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Equal("/mail/accounts?connected=gmail", callback.Headers.Location?.OriginalString);
        Dictionary<string, string> form = Assert.Single(_tokens.Forms);
        Assert.Equal(("authorization_code", "code-1", $"{Browser.Origin}/oauth/callback/google"), (form["grant_type"], form["code"], form["redirect_uri"]));
        Assert.True(form["code_verifier"].Length >= 43);
        JsonElement account = Assert.Single((await _browser.GetFromJsonAsync<List<JsonElement>>(Accounts, Ct))!);
        Assert.Equal(("gmail", "oauth_google", "imap.gmail.com", "lukas@gmail.com"), (
            account.GetProperty("alias").GetString(),
            account.GetProperty("strategy").GetString(),
            account.GetProperty("host").GetString(),
            account.GetProperty("userName").GetString()));
        Assert.Equal("refresh-1", await OwnerSecretAsync("mail_gmail_refresh"));
        Assert.Single(await AuditAsync("mail.account.connect"));
    }

    [Fact]
    public async Task OAuthCallback_StateMustMatchUserAndProviderAndIsSingleUse()
    {
        _tokens.Response = """{"access_token":"ya29","expires_in":3599,"refresh_token":"refresh-1"}""";
        string state = QueryHelpers.ParseQuery((await StartAsync("google", "gmail")).Query)["state"]!;
        (HttpClient other, _) = await Browser.SignInAsync(_factory, "user");

        HttpResponseMessage bogus = await _browser.GetAsync("/oauth/callback/google?code=c&state=forged", Ct);
        HttpResponseMessage otherUser = await other.GetAsync($"/oauth/callback/google?code=c&state={Uri.EscapeDataString(state)}", Ct);

        Assert.Equal("/mail/accounts?error=mail.oauth_state_invalid", bogus.Headers.Location?.OriginalString);
        Assert.Equal("/mail/accounts?error=mail.oauth_state_invalid", otherUser.Headers.Location?.OriginalString);
        Assert.Empty(_tokens.Forms);
        Assert.Empty((await _browser.GetFromJsonAsync<List<JsonElement>>(Accounts, Ct))!);

        string fresh = QueryHelpers.ParseQuery((await StartAsync("google", "gmail2")).Query)["state"]!;
        HttpResponseMessage wrongProvider = await _browser.GetAsync($"/oauth/callback/microsoft?code=c&state={Uri.EscapeDataString(fresh)}", Ct);
        Assert.Equal("/mail/accounts?error=mail.oauth_state_invalid", wrongProvider.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task OAuthCallback_ProviderDenied_ReportsTheError()
    {
        string state = QueryHelpers.ParseQuery((await StartAsync("google", "gmail")).Query)["state"]!;

        HttpResponseMessage denied = await _browser.GetAsync($"/oauth/callback/google?error=access_denied&state={Uri.EscapeDataString(state)}", Ct);

        Assert.Equal("/mail/accounts?error=access_denied", denied.Headers.Location?.OriginalString);
        Assert.Empty(_tokens.Forms);
    }

    [Fact]
    public async Task OAuthStart_UnconfiguredProvider_Unavailable()
    {
        HttpResponseMessage response = await _browser.PostAsJsonAsync("/api/v1/mail/oauth/microsoft/start", new { alias = "work", email = "lukas@contoso.com" }, Ct);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("mail.oauth_not_configured", await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task OAuthCallback_Anonymous_Unauthorized()
    {
        HttpResponseMessage response = await Browser.Create(_factory).GetAsync("/oauth/callback/google?code=c&state=s", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<Uri> StartAsync(string provider, string alias)
    {
        HttpResponseMessage start = await _browser.PostAsJsonAsync($"/api/v1/mail/oauth/{provider}/start", new { alias, email = "lukas@gmail.com" }, Ct);
        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        return new Uri((await start.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("authorizationUrl").GetString()!);
    }

    private async Task<string?> OwnerSecretAsync(string name)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        using IDisposable context = scope.ServiceProvider.GetRequiredService<IExecutionContextAccessor>().Use(new MarshalExecutionContext { OwnerId = _user, Source = SessionSource.Web });
        Result<string> value = await scope.ServiceProvider.GetRequiredService<ISecretStore>().GetForOwnerAsync(SecretName.Create(name).Value, _user, Ct);
        return value.IsSuccess ? value.Value : null;
    }

    private Task<List<AuditEvent>> AuditAsync(string action) =>
        _factory.WithDatabaseAsync(_user, db => db.AuditEvents.Where(entry => entry.Action == action).ToListAsync(Ct));

    private sealed class TokenEndpoint : HttpMessageHandler
    {
        public string Response { get; set; } = """{"error":"invalid_grant"}""";

        public List<Dictionary<string, string>> Forms { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string body = await request.Content!.ReadAsStringAsync(cancellationToken);
            Forms.Add(QueryHelpers.ParseQuery(body).ToDictionary(pair => pair.Key, pair => pair.Value.ToString()));
            return new HttpResponseMessage(Response.Contains("error", StringComparison.Ordinal) ? HttpStatusCode.BadRequest : HttpStatusCode.OK)
            {
                Content = new StringContent(Response, Encoding.UTF8, "application/json"),
            };
        }
    }
}
