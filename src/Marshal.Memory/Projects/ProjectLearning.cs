using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Memory;
using Marshal.Abstractions.Runs;
using Marshal.Data;
using Marshal.Data.Memory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Marshal.Memory.Projects;

/// <summary>
/// Feedback rows and alias learning: phrases of uncorrected or confirmed inferences become alias candidates and are
/// promoted to active aliases after enough confirmations; rejected phrases are negative examples.
/// </summary>
public sealed class ProjectLearning(
    IDbContextFactory<MarshalDbContext> contexts,
    IExecutionContextAccessor executionContext,
    IOptions<MemoryOptions> options,
    TimeProvider timeProvider)
{
    /// <summary>Longest phrase kept for feedback and aliases.</summary>
    public const int MaxPhrase = 120;

    /// <summary>A turn text as a phrase: normalized and cut to <see cref="MaxPhrase"/>.</summary>
    public static string Phrase(string text)
    {
        string normalized = ChunkIds.Normalize(text);
        return normalized.Length <= MaxPhrase ? normalized : normalized[..MaxPhrase].TrimEnd();
    }

    /// <summary>Counts one confirmation of <paramref name="phrase"/> for a project; promotes the candidate when due.</summary>
    public async Task ConfirmAliasAsync(ProjectId project, string phrase, CancellationToken cancellationToken)
    {
        if (phrase.Length == 0)
            return;

        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        ProjectAlias? alias = await db.ProjectAliases.SingleOrDefaultAsync(candidate => candidate.ProjectId == project && candidate.Phrase == phrase, cancellationToken);
        if (alias is null)
        {
            alias = new ProjectAlias
            {
                Id = Guid.CreateVersion7(),
                OwnerId = Owner,
                ProjectId = project,
                Phrase = phrase,
                Status = ProjectAliasStatus.Candidate,
                CreatedAt = timeProvider.GetUtcNow(),
            };
            db.ProjectAliases.Add(alias);
        }

        if (alias.Status == ProjectAliasStatus.Rejected)
            return;

        alias.Confirmations++;
        if (alias.Status == ProjectAliasStatus.Candidate && alias.Confirmations >= options.Value.Projects.AliasPromotion)
            alias.Status = ProjectAliasStatus.Active;

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Stores a correction or confirmation.</summary>
    public async Task RecordAsync(ProjectId project, ProjectFeedbackKind kind, string phrase, float[]? embedding, RunId? run, CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        db.ProjectFeedback.Add(new ProjectFeedback
        {
            Id = Guid.CreateVersion7(),
            OwnerId = Owner,
            ProjectId = project,
            RunId = run,
            Kind = kind,
            Phrase = phrase,
            Embedding = embedding is null ? null : Vectors.ToBlob(embedding),
            CreatedAt = timeProvider.GetUtcNow(),
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    private OwnerId Owner => executionContext.Current?.OwnerId
        ?? throw new InvalidOperationException("Memory accessed without an execution context.");
}
