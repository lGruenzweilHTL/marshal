using Marshal.Abstractions.Identity;
using Microsoft.EntityFrameworkCore;

namespace Marshal.Data.Identity;

/// <summary>
/// All users of the instance.
/// </summary>
public sealed class EfUserDirectory(MarshalDbContext db) : IUserDirectory
{
    public async Task<IReadOnlyList<OwnerId>> ListAsync(CancellationToken cancellationToken) =>
        await db.Users.AsNoTracking().Select(user => user.Id).ToListAsync(cancellationToken);
}
