using Marshal.Abstractions;
using Marshal.Abstractions.Mail;
using Marshal.Integrations.Mail.Credentials;
using Marshal.Testing;
using Microsoft.Extensions.Time.Testing;

namespace Marshal.Integrations.Mail.Tests.Credentials;

public sealed class CredentialSourceTests
{
    private readonly FakeSecretStore _secrets = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 29, 10, 0, 0, TimeSpan.Zero));
    private readonly FakeTokenEndpoint _tokens = new();

    public CredentialSourceTests()
    {
        _secrets.Values["google_client"] = "google-client-secret";
        _secrets.Values["microsoft_client"] = "microsoft-client-secret";
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Password_FromTheOwnerSecret()
    {
        _secrets.Values["mail_private_password"] = "app-password";

        MailCredential credential = (await new PasswordCredentialSource(_secrets).GetAsync(Account(MailCredentialStrategy.Password, "mail_private_password"), Ct)).Value;

        Assert.Equal(new MailCredential("lukas@example.com", "app-password", IsAccessToken: false), credential);
    }

    [Fact]
    public async Task Password_Missing_CredentialMissing()
    {
        Result<MailCredential> result = await new PasswordCredentialSource(_secrets).GetAsync(Account(MailCredentialStrategy.Password, "mail_private_password"), Ct);

        Assert.Equal("mail.credential_missing", result.Error.Code);
    }

    [Fact]
    public async Task Google_RefreshesWithTheClientAndCachesTheAccessToken()
    {
        _secrets.Values["mail_private_refresh"] = "refresh-1";
        _tokens.Returns("access-1", expiresIn: 3600);
        OAuth2CredentialSource source = Source(OAuthPresets.Google);
        MailAccount account = Account(MailCredentialStrategy.OAuthGoogle, "mail_private_refresh");

        MailCredential first = (await source.GetAsync(account, Ct)).Value;
        MailCredential second = (await source.GetAsync(account, Ct)).Value;

        Assert.Equal(new MailCredential("lukas@example.com", "access-1", IsAccessToken: true), first);
        Assert.Equal(first, second);
        (Uri url, Dictionary<string, string> form) = Assert.Single(_tokens.Requests);
        Assert.Equal(new Uri("https://oauth2.googleapis.com/token"), url);
        Assert.Equal(
            new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = "refresh-1",
                ["client_id"] = "google-client-id",
                ["client_secret"] = "google-client-secret",
            },
            form);
    }

    [Fact]
    public async Task AccessToken_RefreshedShortlyBeforeExpiry()
    {
        _secrets.Values["mail_private_refresh"] = "refresh-1";
        _tokens.Returns("access-1", expiresIn: 3600).Returns("access-2", expiresIn: 3600);
        OAuth2CredentialSource source = Source(OAuthPresets.Google);
        MailAccount account = Account(MailCredentialStrategy.OAuthGoogle, "mail_private_refresh");

        await source.GetAsync(account, Ct);
        _time.Advance(TimeSpan.FromMinutes(59));
        MailCredential refreshed = (await source.GetAsync(account, Ct)).Value;

        Assert.Equal("access-2", refreshed.Secret);
        Assert.Equal(2, _tokens.Requests.Count);
    }

    [Fact]
    public async Task Microsoft_RotatedRefreshTokenStoredAsOwnerSecret()
    {
        _secrets.Values["mail_work_refresh"] = "refresh-1";
        _tokens.Returns("access-1", refreshToken: "refresh-2");
        var writer = new FakeSecretWriter(_secrets);
        MailAccount account = Account(MailCredentialStrategy.OAuthMicrosoft, "mail_work_refresh");

        await Source(OAuthPresets.Microsoft, writer).GetAsync(account, Ct);

        (Uri url, Dictionary<string, string> form) = Assert.Single(_tokens.Requests);
        Assert.Equal(new Uri("https://login.microsoftonline.com/common/oauth2/v2.0/token"), url);
        Assert.Equal("https://outlook.office.com/IMAP.AccessAsUser.All offline_access", form["scope"]);
        Assert.Equal(("mail_work_refresh", "refresh-2", (Marshal.Abstractions.Identity.OwnerId?)TestExecutionContext.OwnerA), Assert.Single(writer.Writes));
    }

    [Fact]
    public async Task RevokedRefreshToken_ReauthorizeRequired()
    {
        _secrets.Values["mail_private_refresh"] = "refresh-1";
        _tokens.Fails("invalid_grant");

        Result<MailCredential> result = await Source(OAuthPresets.Google).GetAsync(Account(MailCredentialStrategy.OAuthGoogle, "mail_private_refresh"), Ct);

        Assert.Equal("mail.reauthorize_required", result.Error.Code);
    }

    [Fact]
    public async Task ClientNotConfigured_OAuthNotConfigured()
    {
        _secrets.Values["mail_private_refresh"] = "refresh-1";
        var source = new OAuth2CredentialSource(OAuthPresets.Google, new OAuthTokenClient(_tokens, Options(google: false)), _secrets, new FakeSecretWriter(_secrets), _time);

        Result<MailCredential> result = await source.GetAsync(Account(MailCredentialStrategy.OAuthGoogle, "mail_private_refresh"), Ct);

        Assert.Equal("mail.oauth_not_configured", result.Error.Code);
        Assert.Empty(_tokens.Requests);
    }

    [Fact]
    public void AuthorizationUrl_Google_OfflineConsentPkceAndState()
    {
        Uri url = new OAuthTokenClient(_tokens, Options()).AuthorizationUrl(OAuthPresets.Google, new Uri("https://marshal.example.com/oauth/callback/google"), "state-1", "challenge-1").Value;

        Dictionary<string, string> query = Query(url);
        Assert.Equal("https://accounts.google.com/o/oauth2/v2/auth", url.GetLeftPart(UriPartial.Path));
        Assert.Equal(
            new Dictionary<string, string>
            {
                ["response_type"] = "code",
                ["client_id"] = "google-client-id",
                ["redirect_uri"] = "https://marshal.example.com/oauth/callback/google",
                ["scope"] = "https://mail.google.com/",
                ["state"] = "state-1",
                ["code_challenge"] = "challenge-1",
                ["code_challenge_method"] = "S256",
                ["access_type"] = "offline",
                ["prompt"] = "consent",
            },
            query);
    }

    [Fact]
    public async Task ExchangeCode_ReturnsTheRefreshToken()
    {
        _tokens.Returns("access-1", refreshToken: "refresh-new");

        OAuthTokens tokens = (await new OAuthTokenClient(_tokens, Options()).ExchangeCodeAsync(
            OAuthPresets.Microsoft, "code-1", new Uri("https://marshal.example.com/oauth/callback/microsoft"), "verifier-1", Ct)).Value;

        Assert.Equal("refresh-new", tokens.RefreshToken);
        Dictionary<string, string> form = Assert.Single(_tokens.Requests).Form;
        Assert.Equal(("authorization_code", "code-1", "verifier-1", "https://marshal.example.com/oauth/callback/microsoft"), (form["grant_type"], form["code"], form["code_verifier"], form["redirect_uri"]));
    }

    private OAuth2CredentialSource Source(OAuthPreset preset, FakeSecretWriter? writer = null) =>
        new(preset, new OAuthTokenClient(_tokens, Options()), _secrets, writer ?? new FakeSecretWriter(_secrets), _time);

    private Microsoft.Extensions.Options.IOptions<MailOAuthOptions> Options(bool google = true)
    {
        var options = new MailOAuthOptions
        {
            Microsoft = new OAuthClientOptions { ClientId = "microsoft-client-id", ClientSecret = "microsoft-client-secret" },
        };
        if (google)
            options.Google = new OAuthClientOptions { ClientId = "google-client-id", ClientSecret = "google-client-secret" };
        return Microsoft.Extensions.Options.Options.Create(options);
    }

    private static MailAccount Account(MailCredentialStrategy strategy, string secret) => new(
        MailAccountId.New(), TestExecutionContext.OwnerA, strategy == MailCredentialStrategy.OAuthMicrosoft ? "work" : "private", MailProviderKind.Imap, strategy, secret,
        new MailAccountSettings("imap.example.com", 993, MailSecurity.Tls, "lukas@example.com"), DateTimeOffset.UnixEpoch);

    private static Dictionary<string, string> Query(Uri url) =>
        url.Query.TrimStart('?').Split('&').Select(pair => pair.Split('=', 2)).ToDictionary(pair => pair[0], pair => Uri.UnescapeDataString(pair[1]));
}
