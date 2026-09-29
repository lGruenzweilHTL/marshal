using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Memory;
using Marshal.Data;
using Marshal.Data.Memory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.AI;

namespace Marshal.Memory;

/// <summary>
/// <see cref="IMemoryStore"/> on the main SQLite database. Chunk rows go through EF Core (owner-scoped); the keyword
/// (<c>chunks_fts</c>) and vector (<c>chunk_vectors</c>) indexes are written with raw SQL in the same transaction.
/// Embeddings are computed before the transaction from title and content.
/// </summary>
public sealed class SqliteMemoryStore(
    IDbContextFactory<MarshalDbContext> contexts,
    IEmbeddingGenerator<string, Embedding<float>> embeddings,
    IExecutionContextAccessor executionContext,
    TimeProvider timeProvider) : IMemoryStore
{
    private const int MaxTitle = 200;
    private const int MaxContent = 8000;
    private const int MaxSourceRef = 500;

    public async Task<MemoryChunk?> GetAsync(ChunkId id, CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        Chunk? chunk = await db.Chunks.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        return chunk is null ? null : ToView(chunk);
    }

    public async Task<Result<ChunkWriteResult>> WriteAsync(ChunkDraft draft, ChunkAuthor author, CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        Result<Prepared> prepared = await PrepareAsync(db, draft, cancellationToken);
        if (prepared.IsFailure)
            return prepared.Error;

        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        ChunkWriteResult result = await WriteCoreAsync(db, prepared.Value, author, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<Result<ChunkWriteResult>> SupersedeAsync(ChunkId old, ChunkDraft replacement, ChunkAuthor author, CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        if (!await db.Chunks.AnyAsync(chunk => chunk.Id == old, cancellationToken))
            return NotFound(old);

        Result<Prepared> prepared = await PrepareAsync(db, replacement, cancellationToken);
        if (prepared.IsFailure)
            return prepared.Error;
        if (prepared.Value.Id == old)
            return SelfSupersede();

        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        ChunkWriteResult result = await WriteCoreAsync(db, prepared.Value, author, cancellationToken);
        await AddEdgeAsync(db, result.Id, old, ChunkEdgeKind.Supersedes, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<Result> LinkAsync(ChunkId from, ChunkId to, ChunkEdgeKind kind, CancellationToken cancellationToken)
    {
        if (kind == ChunkEdgeKind.Supersedes && from == to)
            return SelfSupersede();

        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        foreach (ChunkId id in new[] { from, to })
        {
            if (!await db.Chunks.AnyAsync(chunk => chunk.Id == id, cancellationToken))
                return NotFound(id);
        }

        await AddEdgeAsync(db, from, to, kind, cancellationToken);
        return Result.Success();
    }

    public async Task<Result> DeleteAsync(ChunkId id, CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        Chunk? chunk = await db.Chunks.SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        if (chunk is null)
            return NotFound(id);
        if (chunk.DeletedAt is not null)
            return Result.Success();

        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        chunk.DeletedAt = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        await MemoryIndexes.RemoveAsync(db, id, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> SetPinnedAsync(ChunkId id, bool pinned, CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        Chunk? chunk = await db.Chunks.SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        if (chunk is null)
            return NotFound(id);

        chunk.Pinned = pinned;
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<IReadOnlyList<MemoryChunkVersion>> GetVersionsAsync(ChunkId id, CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        List<ChunkVersion> versions = await db.ChunkVersions.AsNoTracking()
            .Where(version => version.ChunkId == id)
            .OrderBy(version => version.ReplacedAt)
            .ToListAsync(cancellationToken);
        return [.. versions.Select(version => new MemoryChunkVersion(version.ChunkId, version.Content, version.Importance, version.SourceRef, version.ReplacedAt))];
    }

    public async Task<IReadOnlyList<MemoryEdge>> GetEdgesAsync(ChunkId id, CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        List<ChunkEdge> edges = await db.ChunkEdges.AsNoTracking()
            .Where(edge => edge.FromId == id || edge.ToId == id)
            .OrderBy(edge => edge.CreatedAt)
            .ToListAsync(cancellationToken);
        return [.. edges.Select(ToView)];
    }

    internal static MemoryChunk ToView(Chunk chunk) => new(
        chunk.Id, chunk.Scope, chunk.ProjectId, chunk.Type, chunk.Title, chunk.Content, chunk.Importance, chunk.Pinned, chunk.SourceRef,
        chunk.CreatedBy, chunk.CreatedAt, chunk.UpdatedAt, chunk.DeletedAt, chunk.LastRetrievedAt);

    internal static MemoryEdge ToView(ChunkEdge edge) => new(edge.FromId, edge.ToId, edge.Kind, edge.CreatedAt);

    private OwnerId Owner => executionContext.Current?.OwnerId
        ?? throw new InvalidOperationException("Memory accessed without an execution context.");

    // Validates and embeds outside the transaction; an unchanged chunk is not embedded again.
    private async Task<Result<Prepared>> PrepareAsync(MarshalDbContext db, ChunkDraft draft, CancellationToken cancellationToken)
    {
        string title = draft.Title.Trim();
        string content = draft.Content.Trim();
        string? sourceRef = string.IsNullOrWhiteSpace(draft.SourceRef) ? null : draft.SourceRef.Trim();
        if (title.Length is 0 or > MaxTitle || ChunkIds.Normalize(title).Length == 0)
            return Invalid($"The title must have 1–{MaxTitle} characters.");
        if (content.Length is 0 or > MaxContent)
            return Invalid($"The content must have 1–{MaxContent} characters.");
        if (draft.Importance is < 1 or > 5)
            return Invalid("The importance must be between 1 and 5.");
        if (sourceRef?.Length > MaxSourceRef)
            return Invalid($"The source reference must have at most {MaxSourceRef} characters.");
        if ((draft.Scope == ChunkScope.Project) != draft.ProjectId.HasValue)
            return Invalid("A project chunk needs a project; a general chunk must not have one.");
        if (draft.ProjectId is { } projectId && !await db.Projects.AnyAsync(project => project.Id == projectId && project.DeletedAt == null, cancellationToken))
            return new Error("memory.project_not_found", $"There is no project {projectId}.", ErrorType.NotFound);

        ChunkId id = ChunkIds.Compute(Owner, draft.Scope, draft.ProjectId, draft.Type, title);
        Chunk? existing = await db.Chunks.SingleOrDefaultAsync(chunk => chunk.Id == id, cancellationToken);
        bool unchanged = existing is { DeletedAt: null }
            && existing.Title == title && existing.Content == content && existing.Importance == draft.Importance && existing.SourceRef == sourceRef;
        float[]? vector = unchanged ? null : (await embeddings.GenerateAsync([MemoryIndexes.EmbeddingText(title, content)], cancellationToken: cancellationToken)).Single().Vector.ToArray();
        return new Prepared(id, draft with { Title = title, Content = content, SourceRef = sourceRef }, existing, vector);
    }

    private async Task<ChunkWriteResult> WriteCoreAsync(MarshalDbContext db, Prepared prepared, ChunkAuthor author, CancellationToken cancellationToken)
    {
        if (prepared.Vector is null)
            return new ChunkWriteResult(prepared.Id, ChunkWriteOutcome.Unchanged);

        await MemoryIndexes.EnsureVectorTableAsync(db, embeddings, prepared.Vector.Length, cancellationToken);
        DateTimeOffset now = timeProvider.GetUtcNow();
        ChunkDraft draft = prepared.Draft;
        Chunk? chunk = prepared.Existing;
        ChunkWriteOutcome outcome;
        if (chunk is null)
        {
            chunk = new Chunk
            {
                Id = prepared.Id,
                OwnerId = Owner,
                Scope = draft.Scope,
                ProjectId = draft.ProjectId,
                Type = draft.Type,
                Title = draft.Title,
                Content = draft.Content,
                Importance = draft.Importance,
                SourceRef = draft.SourceRef,
                CreatedBy = author,
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.Chunks.Add(chunk);
            outcome = ChunkWriteOutcome.Created;
        }
        else
        {
            if (chunk.Content != draft.Content || chunk.Importance != draft.Importance || chunk.SourceRef != draft.SourceRef)
            {
                db.ChunkVersions.Add(new ChunkVersion
                {
                    Id = Guid.CreateVersion7(),
                    OwnerId = chunk.OwnerId,
                    ChunkId = chunk.Id,
                    Content = chunk.Content,
                    Importance = chunk.Importance,
                    SourceRef = chunk.SourceRef,
                    ReplacedAt = now,
                });
            }

            chunk.Title = draft.Title;
            chunk.Content = draft.Content;
            chunk.Importance = draft.Importance;
            chunk.SourceRef = draft.SourceRef;
            chunk.UpdatedAt = now;
            chunk.DeletedAt = null;
            outcome = ChunkWriteOutcome.Updated;
        }

        await db.SaveChangesAsync(cancellationToken);
        await MemoryIndexes.PutAsync(db, chunk, prepared.Vector, cancellationToken);
        return new ChunkWriteResult(prepared.Id, outcome);
    }

    private async Task AddEdgeAsync(MarshalDbContext db, ChunkId from, ChunkId to, ChunkEdgeKind kind, CancellationToken cancellationToken)
    {
        if (await db.ChunkEdges.AnyAsync(edge => edge.FromId == from && edge.ToId == to && edge.Kind == kind, cancellationToken))
            return;

        db.ChunkEdges.Add(new ChunkEdge { FromId = from, ToId = to, Kind = kind, OwnerId = Owner, CreatedAt = timeProvider.GetUtcNow() });
        await db.SaveChangesAsync(cancellationToken);
    }

    private static Error Invalid(string message) => new("memory.invalid_chunk", message, ErrorType.Validation);

    private static Error NotFound(ChunkId id) => new("memory.chunk_not_found", $"There is no chunk {id}.", ErrorType.NotFound);

    private static Error SelfSupersede() => new("memory.self_supersede", "A chunk cannot supersede itself; update it in place instead.", ErrorType.Validation);

    private sealed record Prepared(ChunkId Id, ChunkDraft Draft, Chunk? Existing, float[]? Vector);
}
