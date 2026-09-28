using System.Text;
using Marshal.Abstractions;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Secrets;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Marshal.Data.Secrets;

/// <summary>
/// UI-managed secrets in the <c>secrets</c> table, encrypted with ASP.NET Core Data Protection.
/// Values are write-only for the API: they are only ever decrypted for use.
/// </summary>
public sealed class DatabaseSecretSource(IDbContextFactory<MarshalDbContext> contexts, IDataProtectionProvider protectionProvider, TimeProvider timeProvider) : ISecretSource, ISecretCatalog, ISecretWriter
{
    private const string Purpose = "Marshal.Secrets.v1";

    private readonly IDataProtector _protector = protectionProvider.CreateProtector(Purpose);

    /// <inheritdoc />
    public bool IsOwnerScoped => true;

    /// <inheritdoc />
    public async Task<string?> TryGetAsync(SecretName name, OwnerId? owner, CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        Secret? row = await Find(db, name, owner).AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        return row is null ? null : Encoding.UTF8.GetString(_protector.Unprotect(row.Ciphertext));
    }

    /// <inheritdoc />
    public async Task SetAsync(SecretName name, string value, OwnerId? owner, CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        byte[] ciphertext = _protector.Protect(Encoding.UTF8.GetBytes(value));
        DateTimeOffset now = timeProvider.GetUtcNow();
        Secret? row = await Find(db, name, owner).SingleOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            db.Secrets.Add(new Secret { Id = Guid.CreateVersion7(), OwnerId = owner, Name = name.Value, Ciphertext = ciphertext, CreatedAt = now, UpdatedAt = now });
        }
        else
        {
            row.Ciphertext = ciphertext;
            row.UpdatedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Result> DeleteAsync(SecretName name, OwnerId? owner, CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        Secret? row = await Find(db, name, owner).SingleOrDefaultAsync(cancellationToken);
        if (row is null)
            return new Error("secret.not_found", $"Secret '{name}' was not found.", ErrorType.NotFound);

        db.Secrets.Remove(row);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SecretListing>> ListAsync(OwnerId owner, CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        var rows = await db.Secrets.AsNoTracking()
            .Where(secret => secret.OwnerId == null || secret.OwnerId == owner)
            .Select(secret => new { secret.Name, secret.OwnerId, secret.UpdatedAt })
            .ToListAsync(cancellationToken);
        return [.. rows
            .OrderBy(row => row.Name, StringComparer.Ordinal)
            .Select(row => new SecretListing(SecretName.Create(row.Name).Value, row.OwnerId, SecretOrigin.Database, row.UpdatedAt))];
    }

    private static IQueryable<Secret> Find(MarshalDbContext db, SecretName name, OwnerId? owner) =>
        owner is null
            ? db.Secrets.Where(secret => secret.OwnerId == null && secret.Name == name.Value)
            : db.Secrets.Where(secret => secret.OwnerId == owner && secret.Name == name.Value);
}
