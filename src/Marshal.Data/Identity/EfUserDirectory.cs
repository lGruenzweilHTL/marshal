using Marshal.Abstractions.Identity;
using Microsoft.EntityFrameworkCore;

namespace Marshal.Data.Identity;

/// <summary>
/// All users of the instance.
/// </summary>
public sealed class EfUserDirectory(IDbContextFactory<MarshalDbContext> contexts) : IUserDirectory
{
    public async Task<IReadOnlyList<OwnerId>> ListAsync(CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        return await db.Users.AsNoTracking().Select(user => user.Id).ToListAsync(cancellationToken);
    }
}
