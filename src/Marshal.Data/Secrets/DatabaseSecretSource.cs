using System.Text;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Secrets;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Marshal.Data.Secrets;

/// <summary>
/// UI-managed secrets in the <c>secrets</c> table, encrypted with ASP.NET Core Data Protection.
/// Values are write-only for the API: they are only ever decrypted for use.
/// </summary>
public sealed class DatabaseSecretSource(MarshalDbContext db, IDataProtectionProvider protectionProvider, TimeProvider timeProvider) : ISecretSource
{
    private const string Purpose = "Marshal.Secrets.v1";

    private readonly IDataProtector _protector = protectionProvider.CreateProtector(Purpose);

    /// <inheritdoc />
    public bool IsOwnerScoped => true;

    /// <inheritdoc />
    public async Task<string?> TryGetAsync(SecretName name, OwnerId? owner, CancellationToken cancellationToken)
    {
        Secret? row = await Find(name, owner).AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        return row is null ? null : Encoding.UTF8.GetString(_protector.Unprotect(row.Ciphertext));
    }

    /// <summary>Creates or replaces a secret. <paramref name="owner"/> null writes an instance secret.</summary>
    public async Task SetAsync(SecretName name, string value, OwnerId? owner, CancellationToken cancellationToken)
    {
        byte[] ciphertext = _protector.Protect(Encoding.UTF8.GetBytes(value));
        DateTimeOffset now = timeProvider.GetUtcNow();
        Secret? row = await Find(name, owner).SingleOrDefaultAsync(cancellationToken);
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

    private IQueryable<Secret> Find(SecretName name, OwnerId? owner) =>
        owner is null
            ? db.Secrets.Where(secret => secret.OwnerId == null && secret.Name == name.Value)
            : db.Secrets.Where(secret => secret.OwnerId == owner && secret.Name == name.Value);
}
