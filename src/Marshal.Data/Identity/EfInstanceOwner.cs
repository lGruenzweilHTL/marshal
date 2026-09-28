using Marshal.Abstractions.Identity;
using Microsoft.EntityFrameworkCore;

namespace Marshal.Data.Identity;

/// <summary>
/// The instance owner is the first user created (seeded on first start).
/// </summary>
public sealed class EfInstanceOwner(MarshalDbContext db) : IInstanceOwner
{
    public async Task<OwnerId> GetAsync(CancellationToken cancellationToken) =>
        await db.Users.AsNoTracking().OrderBy(user => user.CreatedAt).Select(user => user.Id).FirstOrDefaultAsync(cancellationToken) is { } owner && owner != default
            ? owner
            : throw new InvalidOperationException("The instance owner has not been created yet.");
}
