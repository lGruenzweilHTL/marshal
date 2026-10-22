using System.Security.Claims;
using Marshal.Abstractions;
using Marshal.Abstractions.Audit;
using Marshal.Abstractions.Tokens;
using Marshal.Api.Auth;
using Marshal.Api.Problems;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Marshal.Api.Security;

/// <summary>
/// The signed-in user's machine tokens (<c>/me/tokens</c>): list, create (plaintext shown once), revoke.
/// </summary>
internal static class TokenEndpoints
{
    public static RouteGroupBuilder MapTokenEndpoints(this RouteGroupBuilder group)
    {
        RouteGroupBuilder tokens = group.MapGroup("/me/tokens").WithTags("Tokens").RequireAuthorization(Policies.Human);
        tokens.MapGet("/", ListAsync).WithName("ListApiTokens")
            .WithDescription("The user's API tokens without secrets; executor callback tokens are read-only.");
        tokens.MapPost("/", CreateAsync).WithName("CreateApiToken")
            .WithDescription("Creates a token with scopes the user holds; the plaintext is returned only in this response.");
        tokens.MapDelete("/{id}", RevokeAsync).WithName("RevokeApiToken")
            .WithDescription("Revokes one of the user's tokens.");
        return group;
    }

    private static async Task<Ok<List<ApiTokenResponse>>> ListAsync(IApiTokenService tokens, CancellationToken ct) =>
        TypedResults.Ok((await tokens.ListAsync(ct)).Select(ApiTokenResponse.From).ToList());

    private static async Task<Results<Created<CreatedApiTokenResponse>, ProblemHttpResult>> CreateAsync(
        CreateApiTokenRequest request, ClaimsPrincipal user, IApiTokenService tokens, IAuditLog audit, CancellationToken ct)
    {
        if (request.Kind is { } kind && kind != ApiTokenKind.Standard)
            return new Error("token.kind_not_creatable", "Only standard tokens can be created here.", ErrorType.Validation).ToProblem();

        IReadOnlySet<string> held = user.Scopes();
        string? escalated = request.Scopes.FirstOrDefault(scope => !held.Contains(scope));
        if (escalated is not null)
            return new Error("token.scope_not_held", $"You cannot grant scope '{escalated}' because you do not hold it.", ErrorType.Forbidden).ToProblem();

        Result<CreatedApiToken> created = await tokens.CreateAsync(request.Name, request.Scopes, request.ExpiresAt, ct);
        if (created.IsFailure)
            return created.Error.ToProblem();

        ApiTokenInfo info = created.Value.Info;
        (AuditActorKind actorKind, string actorId) = user.Actor();
        await audit.RecordAsync(new AuditEntry(actorKind, actorId, "token.create", info.Id.ToString(), new { info.Name, info.Scopes }), ct);
        return TypedResults.Created($"/me/tokens/{info.Id}", new CreatedApiTokenResponse(ApiTokenResponse.From(info), created.Value.Plaintext));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> RevokeAsync(ApiTokenId id, ClaimsPrincipal user, IApiTokenService tokens, IAuditLog audit, CancellationToken ct)
    {
        ApiTokenInfo? token = (await tokens.ListAsync(ct)).FirstOrDefault(candidate => candidate.Id == id);
        if (token is null)
            return new Error("token.not_found", "The token does not exist.", ErrorType.NotFound).ToProblem();
        if (token.Kind != ApiTokenKind.Standard)
            return new Error("token.read_only", "Executor callback tokens are managed by their task.", ErrorType.Validation).ToProblem();

        Result revoked = await tokens.RevokeAsync(id, ct);
        if (revoked.IsFailure)
            return revoked.Error.ToProblem();

        (AuditActorKind actorKind, string actorId) = user.Actor();
        await audit.RecordAsync(new AuditEntry(actorKind, actorId, "token.revoke", id.ToString(), new { token.Name }), ct);
        return TypedResults.NoContent();
    }
}

/// <summary>A machine token without its secret.</summary>
/// <param name="Id">Token id.</param>
/// <param name="Name">Label.</param>
/// <param name="Scopes">Granted scopes.</param>
/// <param name="Kind"><c>standard</c> or <c>executorCallback</c>.</param>
/// <param name="CreatedAt">Creation time.</param>
/// <param name="ExpiresAt">Expiry, if any.</param>
/// <param name="RevokedAt">Revocation time, if revoked.</param>
/// <param name="ReadOnly">True for tokens users cannot manage (executor callback tokens).</param>
public sealed record ApiTokenResponse(
    ApiTokenId Id, string Name, IReadOnlyList<string> Scopes, ApiTokenKind Kind, DateTimeOffset CreatedAt, DateTimeOffset? ExpiresAt, DateTimeOffset? RevokedAt, bool ReadOnly)
{
    public static ApiTokenResponse From(ApiTokenInfo info) =>
        new(info.Id, info.Name, info.Scopes, info.Kind, info.CreatedAt, info.ExpiresAt, info.RevokedAt, info.Kind != ApiTokenKind.Standard);
}

/// <summary>A new token.</summary>
/// <param name="Name">Label, e.g. the client using it.</param>
/// <param name="Scopes">Scopes to grant; each must be one the user holds.</param>
/// <param name="ExpiresAt">Optional expiry.</param>
/// <param name="Kind">Only <c>standard</c> can be created.</param>
public sealed record CreateApiTokenRequest(string Name, IReadOnlyList<string> Scopes, DateTimeOffset? ExpiresAt, ApiTokenKind? Kind);

/// <summary>A created token.</summary>
/// <param name="Info">The stored token.</param>
/// <param name="Token">The plaintext; shown only in this response.</param>
public sealed record CreatedApiTokenResponse(ApiTokenResponse Info, string Token);
