using System.Security.Claims;
using System.Text.RegularExpressions;
using Marshal.Abstractions;
using Marshal.Abstractions.Audit;
using Marshal.Abstractions.Mail;
using Marshal.Abstractions.Secrets;
using Marshal.Api.Auth;
using Marshal.Api.Problems;
using Marshal.Integrations.Mail.Credentials;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Marshal.Api.Mail;

/// <summary>
/// The accounts view: IMAP accounts with a password, and OAuth accounts connected through a server-side flow
/// (authorization code with PKCE; the refresh token never reaches the browser). Passwords and refresh tokens are
/// write-only owner secrets named after the alias. Every change is audited with the user as actor.
/// </summary>
internal static partial class MailAccountEndpoints
{
    /// <summary>Error code of a callback whose state is unknown, expired, used, or from another user or provider.</summary>
    public const string StateInvalid = "mail.oauth_state_invalid";

    private const string CallbackPath = "/oauth/callback";

    private const string AccountsPage = "/mail/accounts";

    public static RouteGroupBuilder MapMailAccountEndpoints(this RouteGroupBuilder group)
    {
        RouteGroupBuilder mail = group.MapGroup("/mail").WithTags("Mail").RequireAuthorization(Policies.Human);
        mail.MapGet("/accounts", ListAsync).WithName("ListMailAccounts")
            .WithDescription("The user's mail accounts without credentials.");
        mail.MapPost("/accounts", CreateAsync).WithName("CreateMailAccount")
            .WithDescription("Adds an IMAP account; the password becomes a write-only owner secret.");
        mail.MapDelete("/accounts/{id}", DeleteAsync).WithName("DeleteMailAccount")
            .WithDescription("Removes an account and its stored credential.");
        mail.MapGet("/oauth/providers", Providers).WithName("ListMailOAuthProviders")
            .WithDescription("OAuth providers and whether this instance has a client for them.");
        mail.MapPost("/oauth/{provider}/start", StartAsync).WithName("StartMailOAuth")
            .WithDescription("Starts connecting an account: returns the provider's sign-in URL for the browser.");
        return group;
    }

    /// <summary>The provider redirects the browser here; it is a page route, not part of the versioned API.</summary>
    public static IEndpointRouteBuilder MapMailOAuthCallback(this IEndpointRouteBuilder app)
    {
        app.MapGet(CallbackPath + "/{provider}", CallbackAsync).RequireAuthorization(Policies.Human).ExcludeFromDescription();
        return app;
    }

    private static async Task<Ok<List<MailAccountItem>>> ListAsync(IMailAccountStore accounts, CancellationToken ct) =>
        TypedResults.Ok((await accounts.ListAsync(ct)).Select(MailAccountItem.From).ToList());

    private static async Task<Results<Created<MailAccountItem>, ProblemHttpResult>> CreateAsync(
        CreateMailAccountRequest request, ClaimsPrincipal user, IMailAccountStore accounts, ISecretWriter secrets, IAuditLog audit, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Host) || request.Port is < 1 or > 65535 || string.IsNullOrWhiteSpace(request.UserName) || string.IsNullOrEmpty(request.Password))
            return new Error("mail.invalid_account", "host, port (1-65535), userName and password are required.", ErrorType.Validation).ToProblem();

        Result<string> alias = MailAccountAlias.Normalize(request.Alias);
        if (alias.IsFailure)
            return alias.Error.ToProblem();

        SecretName secret = SecretFor(alias.Value, "password");
        var settings = new MailAccountSettings(request.Host.Trim(), request.Port, request.Security, request.UserName.Trim(), request.DraftsFolder);
        Result<MailAccount> created = await accounts.CreateAsync(new NewMailAccount(alias.Value, MailProviderKind.Imap, MailCredentialStrategy.Password, secret.Value, settings), ct);
        if (created.IsFailure)
            return created.Error.ToProblem();

        await secrets.SetAsync(secret, request.Password, user.Owner(), ct);
        await AuditAsync(audit, user, "mail.account.create", created.Value, ct);
        return TypedResults.Created($"accounts/{created.Value.Id}", MailAccountItem.From(created.Value));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(
        MailAccountId id, ClaimsPrincipal user, IMailAccountStore accounts, ISecretWriter secrets, IAuditLog audit, CancellationToken ct)
    {
        if (await accounts.GetAsync(id, ct) is not { } account)
            return MailErrors.AccountNotFound(id.ToString()).ToProblem();

        Result deleted = await accounts.DeleteAsync(id, ct);
        if (deleted.IsFailure)
            return deleted.Error.ToProblem();

        if (SecretName.Create(account.CredentialSecret) is { IsSuccess: true } secret)
            await secrets.DeleteAsync(secret.Value, user.Owner(), ct);

        await AuditAsync(audit, user, "mail.account.delete", account, ct);
        return TypedResults.NoContent();
    }

    private static Ok<List<MailOAuthProvider>> Providers(OAuthTokenClient tokens) =>
        TypedResults.Ok(OAuthPresets.All.Select(preset => new MailOAuthProvider(preset.Name, tokens.IsConfigured(preset))).ToList());

    private static async Task<Results<Ok<StartMailOAuthResponse>, ProblemHttpResult>> StartAsync(
        string provider, StartMailOAuthRequest request, ClaimsPrincipal user, HttpContext context, IMailAccountStore accounts,
        OAuthTokenClient tokens, MailOAuthFlows flows, CancellationToken ct)
    {
        if (OAuthPresets.Find(provider) is not { } preset)
            return new Error("mail.oauth_provider_unknown", $"There is no OAuth provider '{provider}'.", ErrorType.NotFound).ToProblem();
        if (!tokens.IsConfigured(preset))
            return MailErrors.OAuthNotConfigured(preset.Name).ToProblem();

        Result<string> alias = MailAccountAlias.Normalize(request.Alias);
        if (alias.IsFailure)
            return alias.Error.ToProblem();
        if (await accounts.FindByAliasAsync(alias.Value, ct) is not null)
            return MailErrors.AliasTaken(alias.Value).ToProblem();
        if (string.IsNullOrWhiteSpace(request.Email) || !request.Email.Contains('@', StringComparison.Ordinal))
            return new Error("mail.invalid_email", "email must be the mailbox address.", ErrorType.Validation).ToProblem();

        (string state, string challenge) = flows.Start(user.Owner(), preset.Name, alias.Value, request.Email.Trim());
        Result<Uri> url = tokens.AuthorizationUrl(preset, RedirectUri(context.Request, preset), state, challenge);
        return url.IsSuccess ? TypedResults.Ok(new StartMailOAuthResponse(url.Value)) : url.Error.ToProblem();
    }

    private static async Task<RedirectHttpResult> CallbackAsync(
        string provider, string? code, string? state, string? error, ClaimsPrincipal user, HttpContext context, IMailAccountStore accounts,
        OAuthTokenClient tokens, MailOAuthFlows flows, ISecretWriter secrets, IAuditLog audit, CancellationToken ct)
    {
        string page = context.Request.PathBase + AccountsPage;
        if (flows.Take(user.Owner(), provider, state) is not { } flow || OAuthPresets.Find(flow.Provider) is not { } preset)
            return Failed(page, StateInvalid);
        if (error is not null)
            return Failed(page, ErrorCode().IsMatch(error) ? error : "mail.oauth_failed");
        if (string.IsNullOrEmpty(code))
            return Failed(page, "mail.oauth_failed");

        Result<OAuthTokens> exchanged = await tokens.ExchangeCodeAsync(preset, code, RedirectUri(context.Request, preset), flow.CodeVerifier, ct);
        if (exchanged.IsFailure)
            return Failed(page, exchanged.Error.Code);
        if (string.IsNullOrEmpty(exchanged.Value.RefreshToken))
            return Failed(page, "mail.oauth_failed");

        SecretName secret = SecretFor(flow.Alias, "refresh");
        var settings = new MailAccountSettings(preset.ImapHost, 993, MailSecurity.Tls, flow.Email);
        Result<MailAccount> created = await accounts.CreateAsync(new NewMailAccount(flow.Alias, MailProviderKind.Imap, preset.Strategy, secret.Value, settings), ct);
        if (created.IsFailure)
            return Failed(page, created.Error.Code);

        await secrets.SetAsync(secret, exchanged.Value.RefreshToken, user.Owner(), ct);
        await AuditAsync(audit, user, "mail.account.connect", created.Value, ct);
        return TypedResults.Redirect($"{page}?connected={Uri.EscapeDataString(created.Value.Alias)}");
    }

    // The provider must see the exact same redirect URI in the authorization request and the code exchange.
    private static Uri RedirectUri(HttpRequest request, OAuthPreset preset) =>
        new($"{request.Scheme}://{request.Host}{request.PathBase}{CallbackPath}/{preset.Name}");

    private static RedirectHttpResult Failed(string page, string code) => TypedResults.Redirect($"{page}?error={Uri.EscapeDataString(code)}");

    // Aliases are 1-32 of [a-z0-9-]: every alias fits a secret name after the prefix.
    private static SecretName SecretFor(string alias, string kind) => SecretName.Create($"mail_{alias.Replace('-', '_')}_{kind}").Value;

    private static Task AuditAsync(IAuditLog audit, ClaimsPrincipal user, string action, MailAccount account, CancellationToken ct)
    {
        (AuditActorKind kind, string actor) = user.Actor();
        return audit.RecordAsync(new AuditEntry(kind, actor, action, account.Alias, new { strategy = MailAccountItem.From(account).Strategy, host = account.Settings.Host }), ct);
    }

    [GeneratedRegex("^[a-z_]{1,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex ErrorCode();
}
