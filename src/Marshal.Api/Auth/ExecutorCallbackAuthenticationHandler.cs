using System.Security.Claims;
using System.Text.Encodings.Web;
using Marshal.Abstractions;
using Marshal.Abstractions.Tokens;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Marshal.Api.Auth;

/// <summary>
/// <c>Authorization: Bearer &lt;token&gt;</c> for executor callbacks: only <c>executor_callback</c> tokens, which carry
/// no scopes. The principal has the token's owner, id and task; the endpoint checks that the token belongs to the
/// session it posts for. A revoked token (session ended) fails here, so the request gets 401.
/// </summary>
internal sealed class ExecutorCallbackAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IApiTokenService tokens) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ExecutorCallback";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? header = Request.Headers.Authorization;
        if (header is null || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.NoResult();

        Result<ApiTokenInfo> token = await tokens.ValidateAsync(header["Bearer ".Length..].Trim(), Context.RequestAborted);
        if (token.IsFailure)
            return AuthenticateResult.Fail(token.Error.Message);
        if (token.Value is not { Kind: ApiTokenKind.ExecutorCallback, TaskId: { } task })
            return AuthenticateResult.Fail("Only executor callback tokens are accepted here.");

        List<Claim> claims =
        [
            new(ClaimTypes.NameIdentifier, token.Value.OwnerId.ToString()),
            new(MarshalClaims.Owner, token.Value.OwnerId.ToString()),
            new(MarshalClaims.TokenId, token.Value.Id.ToString()),
            new(MarshalClaims.Task, task.ToString()),
        ];
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
    }
}
