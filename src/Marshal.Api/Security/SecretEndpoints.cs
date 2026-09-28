using System.Security.Claims;
using Marshal.Abstractions;
using Marshal.Abstractions.Audit;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Secrets;
using Marshal.Api.Auth;
using Marshal.Api.Problems;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Marshal.Api.Security;

/// <summary>
/// Write-only secret management: the user's own secrets (<c>/me/secrets</c>) and instance secrets
/// (<c>/secrets/instance</c>, admin only). No endpoint returns a value; every change is audited without it.
/// </summary>
internal static class SecretEndpoints
{
    public static RouteGroupBuilder MapSecretEndpoints(this RouteGroupBuilder group)
    {
        RouteGroupBuilder own = group.MapGroup("/me/secrets").WithTags("Secrets").RequireAuthorization(Policies.Human);
        own.MapGet("/", OverviewAsync).WithName("GetSecretOverview")
            .WithSummary("Every secret visible to the user (own, instance, environment) without values, plus unresolved references.");
        own.MapPut("/{name}", (string name, SecretValueRequest request, ClaimsPrincipal user, ISecretWriter writer, IAuditLog audit, CancellationToken ct) =>
                SetAsync(name, request, user.Owner(), user, writer, audit, ct))
            .WithName("SetOwnSecret").WithSummary("Creates or replaces one of the user's secrets.");
        own.MapDelete("/{name}", (string name, ClaimsPrincipal user, ISecretWriter writer, IAuditLog audit, CancellationToken ct) =>
                DeleteAsync(name, user.Owner(), user, writer, audit, ct))
            .WithName("DeleteOwnSecret").WithSummary("Deletes one of the user's secrets.");

        RouteGroupBuilder instance = group.MapGroup("/secrets/instance").WithTags("Secrets").RequireAuthorization(Policies.Human, Policies.Admin);
        instance.MapGet("/", InstanceAsync).WithName("ListInstanceSecrets")
            .WithSummary("Instance secrets (database and environment) without values.");
        instance.MapPut("/{name}", (string name, SecretValueRequest request, ClaimsPrincipal user, ISecretWriter writer, IAuditLog audit, CancellationToken ct) =>
                SetAsync(name, request, null, user, writer, audit, ct))
            .WithName("SetInstanceSecret").WithSummary("Creates or replaces an instance secret.");
        instance.MapDelete("/{name}", (string name, ClaimsPrincipal user, ISecretWriter writer, IAuditLog audit, CancellationToken ct) =>
                DeleteAsync(name, null, user, writer, audit, ct))
            .WithName("DeleteInstanceSecret").WithSummary("Deletes an instance secret.");
        return group;
    }

    private static async Task<Ok<SecretOverview>> OverviewAsync(
        ClaimsPrincipal user, IEnumerable<ISecretCatalog> catalogs, IEnumerable<ISecretReferenceSource> references, ISecretStore store, CancellationToken ct)
    {
        OwnerId owner = user.Owner();
        bool admin = user.IsInRole(Roles.Admin);
        List<SecretItem> secrets = [.. (await ListAsync(catalogs, owner, ct)).Select(entry => SecretItem.From(entry, editable: entry.Origin == SecretOrigin.Database && (entry.Owner is not null || admin)))];

        var unresolved = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (ISecretReferenceSource source in references)
        {
            foreach (SecretReference reference in await source.ListAsync(owner, ct))
            {
                Result<string> resolved = reference.OwnerScoped ? await store.GetForOwnerAsync(reference.Name, owner, ct) : await store.GetAsync(reference.Name, ct);
                if (resolved.IsFailure)
                    (unresolved.TryGetValue(reference.Name.Value, out List<string>? where) ? where : unresolved[reference.Name.Value] = []).Add(reference.Location);
            }
        }

        return TypedResults.Ok(new SecretOverview(secrets, [.. unresolved.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => new UnresolvedSecret(pair.Key, pair.Value))]));
    }

    private static async Task<Ok<List<SecretItem>>> InstanceAsync(ClaimsPrincipal user, IEnumerable<ISecretCatalog> catalogs, CancellationToken ct) =>
        TypedResults.Ok((await ListAsync(catalogs, user.Owner(), ct))
            .Where(entry => entry.Owner is null)
            .Select(entry => SecretItem.From(entry, editable: entry.Origin == SecretOrigin.Database))
            .ToList());

    private static async Task<Results<NoContent, ProblemHttpResult>> SetAsync(
        string name, SecretValueRequest request, OwnerId? owner, ClaimsPrincipal user, ISecretWriter writer, IAuditLog audit, CancellationToken ct)
    {
        Result<SecretName> secretName = SecretName.Create(name);
        if (secretName.IsFailure)
            return secretName.Error.ToProblem();
        if (string.IsNullOrEmpty(request.Value))
            return new Error("secret.value_required", "value is required.", ErrorType.Validation).ToProblem();

        await writer.SetAsync(secretName.Value, request.Value, owner, ct);
        await AuditAsync(audit, user, "secret.set", secretName.Value, owner, ct);
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(
        string name, OwnerId? owner, ClaimsPrincipal user, ISecretWriter writer, IAuditLog audit, CancellationToken ct)
    {
        Result<SecretName> secretName = SecretName.Create(name);
        if (secretName.IsFailure)
            return secretName.Error.ToProblem();

        Result deleted = await writer.DeleteAsync(secretName.Value, owner, ct);
        if (deleted.IsFailure)
            return deleted.Error.ToProblem();

        await AuditAsync(audit, user, "secret.delete", secretName.Value, owner, ct);
        return TypedResults.NoContent();
    }

    private static async Task<List<SecretListing>> ListAsync(IEnumerable<ISecretCatalog> catalogs, OwnerId owner, CancellationToken ct)
    {
        var listed = new List<SecretListing>();
        foreach (ISecretCatalog catalog in catalogs)
            listed.AddRange(await catalog.ListAsync(owner, ct));

        return [.. listed.OrderBy(entry => entry.Name.Value, StringComparer.Ordinal).ThenBy(entry => entry.Owner is null)];
    }

    private static Task AuditAsync(IAuditLog audit, ClaimsPrincipal user, string action, SecretName name, OwnerId? owner, CancellationToken ct)
    {
        (AuditActorKind actorKind, string actorId) = user.Actor();
        return audit.RecordAsync(new AuditEntry(actorKind, actorId, action, name.Value, new { scope = owner is null ? "instance" : "user" }), ct);
    }
}

/// <summary>A secret value to store. It is never returned.</summary>
public sealed record SecretValueRequest(string Value);

/// <summary>The secrets view: every visible secret and every reference that does not resolve.</summary>
public sealed record SecretOverview(IReadOnlyList<SecretItem> Secrets, IReadOnlyList<UnresolvedSecret> Unresolved);

/// <summary>A secret without its value.</summary>
/// <param name="Name">Secret name.</param>
/// <param name="Scope"><c>user</c> or <c>instance</c>.</param>
/// <param name="Origin"><c>database</c> (UI-managed) or <c>environment</c> (variable or <c>/run/secrets</c> file).</param>
/// <param name="UpdatedAt">Last change of a database secret.</param>
/// <param name="ReadOnly">True when the current user cannot change it here.</param>
public sealed record SecretItem(string Name, string Scope, SecretOrigin Origin, DateTimeOffset? UpdatedAt, bool ReadOnly)
{
    public static SecretItem From(SecretListing entry, bool editable) =>
        new(entry.Name.Value, entry.Owner is null ? "instance" : "user", entry.Origin, entry.UpdatedAt, !editable);
}

/// <summary>A referenced secret that no source provides.</summary>
/// <param name="Name">Secret name.</param>
/// <param name="ReferencedBy">Where it is referenced.</param>
public sealed record UnresolvedSecret(string Name, IReadOnlyList<string> ReferencedBy);
