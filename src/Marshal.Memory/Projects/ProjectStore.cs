using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Memory;
using Marshal.Data;
using Marshal.Data.Memory;
using Microsoft.EntityFrameworkCore;

namespace Marshal.Memory.Projects;

/// <summary>
/// Projects, aliases and the correction log for the memory view. Names are unique among live projects (compared
/// normalized, like explicit activation does).
/// </summary>
public sealed class ProjectStore(IDbContextFactory<MarshalDbContext> contexts, IExecutionContextAccessor executionContext, TimeProvider timeProvider) : IProjectStore
{
    private const int MaxName = 100;
    private const int MaxDescription = 2000;

    public async Task<IReadOnlyList<ProjectView>> ListAsync(CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        List<Project> projects = await db.Projects.AsNoTracking().Where(project => project.DeletedAt == null).ToListAsync(cancellationToken);
        var views = new List<ProjectView>(projects.Count);
        foreach (Project project in projects.OrderBy(project => project.Name, StringComparer.OrdinalIgnoreCase))
            views.Add(await ViewAsync(db, project, cancellationToken));

        return views;
    }

    public async Task<Result<ProjectView>> CreateAsync(string name, string description, CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        if (await ValidateAsync(db, null, name, description, cancellationToken) is { } error)
            return error;

        var project = new Project
        {
            Id = ProjectId.New(),
            OwnerId = Owner,
            Name = name.Trim(),
            Description = description.Trim(),
            CreatedAt = timeProvider.GetUtcNow(),
        };
        db.Projects.Add(project);
        await db.SaveChangesAsync(cancellationToken);
        return await ViewAsync(db, project, cancellationToken);
    }

    public async Task<Result<ProjectView>> UpdateAsync(ProjectId id, string name, string description, CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        Project? project = await FindAsync(db, id, cancellationToken);
        if (project is null)
            return NotFound(id);
        if (await ValidateAsync(db, id, name, description, cancellationToken) is { } error)
            return error;

        project.Name = name.Trim();
        project.Description = description.Trim();
        project.Centroid = null;
        await db.SaveChangesAsync(cancellationToken);
        return await ViewAsync(db, project, cancellationToken);
    }

    public async Task<Result> DeleteAsync(ProjectId id, CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        Project? project = await FindAsync(db, id, cancellationToken);
        if (project is null)
            return NotFound(id);

        project.DeletedAt = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<ProjectAliasView>> AddAliasAsync(ProjectId id, string phrase, CancellationToken cancellationToken)
    {
        string normalized = ProjectLearning.Phrase(phrase);
        if (normalized.Length == 0)
            return new Error("memory.invalid_alias", "The alias must contain letters or digits.", ErrorType.Validation);

        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        if (await FindAsync(db, id, cancellationToken) is null)
            return NotFound(id);

        List<ProjectId> live = await db.Projects.Where(project => project.DeletedAt == null && project.Id != id).Select(project => project.Id).ToListAsync(cancellationToken);
        if (await db.ProjectAliases.AnyAsync(alias => alias.Phrase == normalized && alias.Status == ProjectAliasStatus.Active && live.Contains(alias.ProjectId), cancellationToken))
            return new Error("memory.alias_taken", $"'{normalized}' is already an alias of another project.", ErrorType.Conflict);

        ProjectAlias? existing = await db.ProjectAliases.SingleOrDefaultAsync(alias => alias.ProjectId == id && alias.Phrase == normalized, cancellationToken);
        if (existing is null)
        {
            existing = new ProjectAlias
            {
                Id = Guid.CreateVersion7(),
                OwnerId = Owner,
                ProjectId = id,
                Phrase = normalized,
                Status = ProjectAliasStatus.Active,
                CreatedAt = timeProvider.GetUtcNow(),
            };
            db.ProjectAliases.Add(existing);
        }

        existing.Status = ProjectAliasStatus.Active;
        await db.SaveChangesAsync(cancellationToken);
        return View(existing);
    }

    public async Task<Result<ProjectAliasView>> SetAliasStatusAsync(ProjectId id, Guid aliasId, ProjectAliasStatus status, CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        ProjectAlias? alias = await FindAliasAsync(db, id, aliasId, cancellationToken);
        if (alias is null)
            return AliasNotFound();

        alias.Status = status;
        await db.SaveChangesAsync(cancellationToken);
        return View(alias);
    }

    public async Task<Result> RemoveAliasAsync(ProjectId id, Guid aliasId, CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        ProjectAlias? alias = await FindAliasAsync(db, id, aliasId, cancellationToken);
        if (alias is null)
            return AliasNotFound();

        db.ProjectAliases.Remove(alias);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<ProjectFeedbackView>>> GetFeedbackAsync(ProjectId id, CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        if (!await db.Projects.AnyAsync(project => project.Id == id, cancellationToken))
            return NotFound(id);

        List<ProjectFeedback> rows = await db.ProjectFeedback.AsNoTracking().Where(row => row.ProjectId == id).ToListAsync(cancellationToken);
        return rows.OrderByDescending(row => row.CreatedAt)
            .Select(row => new ProjectFeedbackView(row.Id, row.Kind, row.Phrase, row.RunId, row.CreatedAt))
            .ToList();
    }

    private OwnerId Owner => executionContext.Current?.OwnerId
        ?? throw new InvalidOperationException("Projects accessed without an execution context.");

    private static async Task<Error?> ValidateAsync(MarshalDbContext db, ProjectId? self, string name, string description, CancellationToken cancellationToken)
    {
        string trimmed = name.Trim();
        if (trimmed.Length is 0 or > MaxName || ChunkIds.Normalize(trimmed).Length == 0)
            return new Error("memory.invalid_project", $"The name must have 1–{MaxName} characters.", ErrorType.Validation);
        if (description.Trim().Length > MaxDescription)
            return new Error("memory.invalid_project", $"The description must have at most {MaxDescription} characters.", ErrorType.Validation);

        List<string> names = await db.Projects.Where(project => project.DeletedAt == null && project.Id != self).Select(project => project.Name).ToListAsync(cancellationToken);
        return names.Any(other => ChunkIds.Normalize(other) == ChunkIds.Normalize(trimmed))
            ? new Error("memory.project_exists", $"There is already a project named '{trimmed}'.", ErrorType.Conflict)
            : null;
    }

    private static Task<Project?> FindAsync(MarshalDbContext db, ProjectId id, CancellationToken cancellationToken) =>
        db.Projects.SingleOrDefaultAsync(project => project.Id == id && project.DeletedAt == null, cancellationToken);

    private static Task<ProjectAlias?> FindAliasAsync(MarshalDbContext db, ProjectId id, Guid aliasId, CancellationToken cancellationToken) =>
        db.ProjectAliases.SingleOrDefaultAsync(alias => alias.Id == aliasId && alias.ProjectId == id, cancellationToken);

    private static async Task<ProjectView> ViewAsync(MarshalDbContext db, Project project, CancellationToken cancellationToken)
    {
        List<ProjectAlias> aliases = await db.ProjectAliases.AsNoTracking().Where(alias => alias.ProjectId == project.Id).ToListAsync(cancellationToken);
        int chunks = await db.Chunks.CountAsync(chunk => chunk.ProjectId == project.Id && chunk.DeletedAt == null, cancellationToken);
        return new ProjectView(project.Id, project.Name, project.Description, project.CreatedAt, project.LastActiveAt, chunks,
            [.. aliases.OrderBy(alias => alias.Status).ThenBy(alias => alias.Phrase, StringComparer.Ordinal).Select(View)], project.Summary, project.SummaryUpdatedAt);
    }

    private static ProjectAliasView View(ProjectAlias alias) => new(alias.Id, alias.Phrase, alias.Status, alias.Confirmations, alias.CreatedAt);

    private static Error NotFound(ProjectId id) => new("memory.project_not_found", $"There is no project {id}.", ErrorType.NotFound);

    private static Error AliasNotFound() => new("memory.alias_not_found", "The alias does not exist.", ErrorType.NotFound);
}
