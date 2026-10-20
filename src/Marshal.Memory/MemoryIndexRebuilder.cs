using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Identity;
using Marshal.Data;
using Marshal.Data.Memory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Marshal.Memory;

/// <summary>
/// Re-embeds memory when the configured embedding model differs from the one the vector index was built with: every
/// owner's live chunks, the embeddings of project feedback phrases (negative examples), and drops project centroids
/// (recomputed on the next activation). The index is marked pending until the rebuild completes, so an interrupted
/// rebuild starts again on the next start.
/// </summary>
public sealed partial class MemoryIndexRebuilder(
    IDbContextFactory<MarshalDbContext> contexts,
    IEmbeddingGenerator<string, Embedding<float>> embeddings,
    IUserDirectory users,
    IExecutionContextAccessor executionContext,
    ILogger<MemoryIndexRebuilder> logger)
{
    private const string PendingModel = "rebuild-pending";
    private const int BatchSize = 32;

    /// <summary>Rebuilds the index if it was built with another model; false when nothing was to do.</summary>
    public async Task<bool> RebuildIfStaleAsync(CancellationToken cancellationToken)
    {
        string model = MemoryIndexes.ModelName(embeddings);
        await using (MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken))
        {
            if (await MemoryIndexes.ReadInfoAsync(db, cancellationToken) is not { } info || info.Model == model)
                return false;

            LogRebuilding(logger, info.Model, model);
            await SetModelAsync(db, PendingModel, cancellationToken);
        }

        int? dimensions = null;
        int chunks = 0;
        foreach (OwnerId owner in await users.ListAsync(cancellationToken))
        {
            using IDisposable scope = executionContext.Use(new MarshalExecutionContext { OwnerId = owner, Source = SessionSource.Task, IsInteractive = false });
            await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
            List<Chunk> live = await db.Chunks.Where(chunk => chunk.DeletedAt == null).ToListAsync(cancellationToken);
            foreach (Chunk[] batch in live.Chunk(BatchSize))
            {
                IReadOnlyList<float[]> vectors = await EmbedAsync(batch.Select(chunk => MemoryIndexes.EmbeddingText(chunk.Title, chunk.Content)), cancellationToken);
                if (dimensions is null)
                {
                    dimensions = vectors[0].Length;
                    await MemoryIndexes.CreateVectorTableAsync(db, PendingModel, dimensions.Value, cancellationToken);
                }

                for (int i = 0; i < batch.Length; i++)
                    await MemoryIndexes.PutAsync(db, batch[i], vectors[i], cancellationToken);
            }

            chunks += live.Count;
            List<ProjectFeedback> feedback = await db.ProjectFeedback.Where(row => row.Embedding != null).ToListAsync(cancellationToken);
            foreach (ProjectFeedback[] batch in feedback.Chunk(BatchSize))
            {
                IReadOnlyList<float[]> vectors = await EmbedAsync(batch.Select(row => row.Phrase), cancellationToken);
                for (int i = 0; i < batch.Length; i++)
                    batch[i].Embedding = Vectors.ToBlob(vectors[i]);
            }

            await db.SaveChangesAsync(cancellationToken);
            await db.Projects.ExecuteUpdateAsync(update => update.SetProperty(project => project.Centroid, (byte[]?)null), cancellationToken);
        }

        await using (MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken))
        {
            if (dimensions is null)
            {
                // No chunks at all: the next write creates the table for the new model.
                await MemoryIndexes.ExecuteAsync(db, $"DROP TABLE IF EXISTS {MemoryIndexes.VectorTable}", cancellationToken);
                await MemoryIndexes.ExecuteAsync(db, "DELETE FROM memory_vector_index", cancellationToken);
            }
            else
            {
                await SetModelAsync(db, model, cancellationToken);
            }
        }

        LogRebuilt(logger, chunks, model);
        return true;
    }

    /// <summary>
    /// Adds keyword entries for chunk versions that have none (versions written before the version index existed);
    /// returns how many were added.
    /// </summary>
    public async Task<int> IndexVersionsAsync(CancellationToken cancellationToken)
    {
        int added = 0;
        foreach (OwnerId owner in await users.ListAsync(cancellationToken))
        {
            using IDisposable scope = executionContext.Use(new MarshalExecutionContext { OwnerId = owner, Source = SessionSource.Task, IsInteractive = false });
            await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
            HashSet<string> indexed = [];
            await using (System.Data.Common.DbCommand command = MemoryIndexes.Command(db, "SELECT version_id FROM chunk_versions_fts WHERE owner_id = $owner", ("$owner", MemoryIndexes.OwnerKey(owner))))
            await using (System.Data.Common.DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                    indexed.Add(reader.GetString(0));
            }

            foreach (ChunkVersion version in await db.ChunkVersions.AsNoTracking().ToListAsync(cancellationToken))
            {
                if (indexed.Contains(version.Id.ToString("D")))
                    continue;

                await MemoryIndexes.PutVersionAsync(db, version, cancellationToken);
                added++;
            }
        }

        if (added > 0)
            LogVersionsIndexed(logger, added);

        return added;
    }

    private async Task<IReadOnlyList<float[]>> EmbedAsync(IEnumerable<string> texts, CancellationToken cancellationToken) =>
        [.. (await embeddings.GenerateAsync(texts, cancellationToken: cancellationToken)).Select(embedding => embedding.Vector.ToArray())];

    private static Task SetModelAsync(MarshalDbContext db, string model, CancellationToken cancellationToken) =>
        MemoryIndexes.ExecuteAsync(db, "UPDATE memory_vector_index SET model = $model WHERE id = 1", cancellationToken, ("$model", model));

    [LoggerMessage(Level = LogLevel.Warning, Message = "Memory index was built with {OldModel}; re-embedding memory with {NewModel}")]
    private static partial void LogRebuilding(ILogger logger, string oldModel, string newModel);

    [LoggerMessage(Level = LogLevel.Information, Message = "Added {Count} chunk versions to the version keyword index")]
    private static partial void LogVersionsIndexed(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Re-embedded {Count} memory chunks with {Model}")]
    private static partial void LogRebuilt(ILogger logger, int count, string model);
}

/// <summary>
/// Runs <see cref="MemoryIndexRebuilder"/> once at startup (after the database is migrated), then indexes versions
/// missing from the version keyword index. A failure (e.g. the
/// embedding server is down) is logged; the rebuild runs again on the next start.
/// </summary>
public sealed partial class MemoryIndexStartup(MemoryIndexRebuilder rebuilder, ILogger<MemoryIndexStartup> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await rebuilder.RebuildIfStaleAsync(cancellationToken);
            await rebuilder.IndexVersionsAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogRebuildFailed(logger, exception);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Error, Message = "Re-embedding memory failed; retrying on the next start")]
    private static partial void LogRebuildFailed(ILogger logger, Exception exception);
}
