using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Runs;
using Marshal.Abstractions.Suggestions;
using Marshal.Data;
using Marshal.Data.Suggestions;
using Microsoft.EntityFrameworkCore;

namespace Marshal.Tasks.SkillProposals;

/// <summary>A skill proposal as the Inbox shows it.</summary>
public sealed record SkillProposalRecord(
    Guid Id,
    string Name,
    string Description,
    string Content,
    string? Diff,
    IReadOnlyList<RunId> SourceRuns,
    SkillProposalStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DecidedAt);

/// <summary>A new row: a draft, or a covered workflow (empty name and content).</summary>
public sealed record NewSkillProposal(string Name, string Description, string Content, string? Diff, IReadOnlyList<RunId> SourceRuns, string SequenceHash, SkillProposalStatus Status);

/// <summary>The current owner's skill proposals.</summary>
public interface ISkillProposalStore
{
    /// <summary>Open and downloaded proposals, newest first.</summary>
    Task<IReadOnlyList<SkillProposalRecord>> ListAsync(CancellationToken cancellationToken);

    Task<SkillProposalRecord?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Whether any row (in any status) exists for the tool sequence.</summary>
    Task<bool> HasSequenceAsync(string sequenceHash, CancellationToken cancellationToken);

    Task<SkillProposalRecord> AddAsync(NewSkillProposal proposal, CancellationToken cancellationToken);

    /// <summary>Marks an open proposal downloaded; a downloaded one stays so.</summary>
    Task<Result<SkillProposalRecord>> MarkDownloadedAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Dismisses a shown proposal; its workflow is not proposed again.</summary>
    Task<Result<SkillProposalRecord>> DismissAsync(Guid id, CancellationToken cancellationToken);
}

/// <summary>Skill proposals in the database, scoped to the current owner by the query filter.</summary>
public sealed class EfSkillProposalStore(IDbContextFactory<MarshalDbContext> contexts, IExecutionContextAccessor executionContext, TimeProvider time) : ISkillProposalStore
{
    public async Task<IReadOnlyList<SkillProposalRecord>> ListAsync(CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        List<SkillProposal> rows = await db.SkillProposals.AsNoTracking()
            .Where(row => row.Status == SkillProposalStatus.Open || row.Status == SkillProposalStatus.Downloaded)
            .ToListAsync(cancellationToken);
        return [.. rows.OrderByDescending(row => row.CreatedAt).Select(ToRecord)];
    }

    public async Task<SkillProposalRecord?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        SkillProposal? row = await db.SkillProposals.AsNoTracking().FirstOrDefaultAsync(row => row.Id == id && row.Status != SkillProposalStatus.Covered, cancellationToken);
        return row is null ? null : ToRecord(row);
    }

    public async Task<bool> HasSequenceAsync(string sequenceHash, CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        return await db.SkillProposals.AnyAsync(row => row.SequenceHash == sequenceHash, cancellationToken);
    }

    public async Task<SkillProposalRecord> AddAsync(NewSkillProposal proposal, CancellationToken cancellationToken)
    {
        DateTimeOffset now = time.GetUtcNow();
        var row = new SkillProposal
        {
            Id = Guid.CreateVersion7(now),
            OwnerId = executionContext.Current?.OwnerId ?? throw new InvalidOperationException("Skill proposal stored without an execution context."),
            Name = proposal.Name,
            Description = proposal.Description,
            Content = proposal.Content,
            Diff = proposal.Diff,
            SourceRunIds = JsonSerializer.Serialize(proposal.SourceRuns.Select(run => run.ToString())),
            SequenceHash = proposal.SequenceHash,
            Status = proposal.Status,
            CreatedAt = now,
        };
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        db.SkillProposals.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        return ToRecord(row);
    }

    public Task<Result<SkillProposalRecord>> MarkDownloadedAsync(Guid id, CancellationToken cancellationToken) =>
        MoveAsync(id, SkillProposalStatus.Downloaded, cancellationToken);

    public Task<Result<SkillProposalRecord>> DismissAsync(Guid id, CancellationToken cancellationToken) =>
        MoveAsync(id, SkillProposalStatus.Dismissed, cancellationToken);

    private async Task<Result<SkillProposalRecord>> MoveAsync(Guid id, SkillProposalStatus status, CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        SkillProposal? row = await db.SkillProposals.FirstOrDefaultAsync(row => row.Id == id && row.Status != SkillProposalStatus.Covered, cancellationToken);
        if (row is null)
            return SkillProposalErrors.NotFound();
        if (row.Status == SkillProposalStatus.Dismissed)
            return SkillProposalErrors.Dismissed();

        if (row.Status != status)
        {
            row.Status = status;
            row.DecidedAt = time.GetUtcNow();
            await db.SaveChangesAsync(cancellationToken);
        }

        return ToRecord(row);
    }

    private static SkillProposalRecord ToRecord(SkillProposal row) => new(
        row.Id,
        row.Name,
        row.Description,
        row.Content,
        row.Diff,
        [.. (JsonSerializer.Deserialize<string[]>(row.SourceRunIds) ?? []).Select(id => new RunId(Guid.Parse(id)))],
        row.Status,
        row.CreatedAt,
        row.DecidedAt);
}

/// <summary>Errors of skill proposal actions.</summary>
public static class SkillProposalErrors
{
    public static Error NotFound() => new("skill_proposal.not_found", "There is no such skill proposal.", ErrorType.NotFound);

    public static Error Dismissed() => new("skill_proposal.dismissed", "The skill proposal was dismissed.", ErrorType.Conflict);
}
