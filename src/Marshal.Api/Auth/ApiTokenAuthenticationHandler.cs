using System.Security.Claims;
using System.Text.Encodings.Web;
using Marshal.Abstractions;
using Marshal.Abstractions.Tokens;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Marshal.Api.Auth;

/// <summary>
/// <c>Authorization: Bearer &lt;token&gt;</c> for machine clients. The principal carries the token's owner and scopes,
/// so token and cookie principals go through the same scope policies. Executor callback tokens are refused here.
/// </summary>
internal sealed class ApiTokenAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IApiTokenService tokens) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ApiToken";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? header = Request.Headers.Authorization;
        if (header is null || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.NoResult();

        Result<ApiTokenInfo> token = await tokens.ValidateAsync(header["Bearer ".Length..].Trim(), Context.RequestAborted);
        if (token.IsFailure)
            return AuthenticateResult.Fail(token.Error.Message);
        // Callback tokens authenticate only on the executor callback endpoint (ExecutorCallbackAuthenticationHandler).
        if (token.Value.Kind != ApiTokenKind.Standard)
            return AuthenticateResult.Fail("This token is only valid for executor callbacks.");

        List<Claim> claims =
        [
            new(ClaimTypes.NameIdentifier, token.Value.OwnerId.ToString()),
            new(MarshalClaims.Owner, token.Value.OwnerId.ToString()),
            new(MarshalClaims.TokenId, token.Value.Id.ToString()),
            .. token.Value.Scopes.Select(scope => new Claim(MarshalClaims.Scope, scope)),
        ];
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
    }
}
