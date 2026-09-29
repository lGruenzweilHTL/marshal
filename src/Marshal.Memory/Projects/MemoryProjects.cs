using Marshal.Abstractions.Memory;
using Marshal.Data;
using Marshal.Data.Memory;
using Microsoft.EntityFrameworkCore;

namespace Marshal.Memory.Projects;

/// <summary>
/// Finds the current owner's live projects by name or active alias (normalized like chunk titles).
/// </summary>
public interface IMemoryProjects
{
    /// <summary>The project named <paramref name="nameOrAlias"/>, or null.</summary>
    Task<ProjectId?> FindAsync(string nameOrAlias, CancellationToken cancellationToken);

    /// <summary>True when the owner has at least one live project.</summary>
    Task<bool> AnyAsync(CancellationToken cancellationToken);
}

/// <inheritdoc />
public sealed class MemoryProjects(IDbContextFactory<MarshalDbContext> contexts) : IMemoryProjects
{
    public async Task<ProjectId?> FindAsync(string nameOrAlias, CancellationToken cancellationToken)
    {
        string wanted = ChunkIds.Normalize(nameOrAlias);
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        List<Project> projects = await db.Projects.AsNoTracking().Where(project => project.DeletedAt == null).ToListAsync(cancellationToken);
        if (projects.FirstOrDefault(project => ChunkIds.Normalize(project.Name) == wanted) is { } byName)
            return byName.Id;

        List<ProjectId> live = [.. projects.Select(project => project.Id)];
        List<ProjectAlias> aliases = await db.ProjectAliases.AsNoTracking()
            .Where(alias => alias.Status == ProjectAliasStatus.Active && live.Contains(alias.ProjectId))
            .ToListAsync(cancellationToken);
        return aliases.FirstOrDefault(alias => ChunkIds.Normalize(alias.Phrase) == wanted)?.ProjectId;
    }

    public async Task<bool> AnyAsync(CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        return await db.Projects.AnyAsync(project => project.DeletedAt == null, cancellationToken);
    }
}
