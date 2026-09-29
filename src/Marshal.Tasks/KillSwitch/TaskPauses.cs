using Marshal.Abstractions.Identity;
using Marshal.Data;
using Microsoft.EntityFrameworkCore;

namespace Marshal.Tasks.KillSwitch;

/// <summary>
/// Reads whether an owner's kill switch is active. Separate from <see cref="EfKillSwitch"/> so the scheduler can
/// check it without depending on the switch (which depends on the scheduler).
/// </summary>
public sealed class TaskPauses(IDbContextFactory<MarshalDbContext> contexts)
{
    public async Task<bool> IsPausedAsync(OwnerId owner, CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        return await db.UserPreferences.AnyAsync(row => row.UserId == owner && row.TasksPausedAt != null, cancellationToken);
    }
}
