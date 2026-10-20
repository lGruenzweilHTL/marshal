using System.Data.Common;
using System.Runtime.InteropServices;
using Marshal.Abstractions.Memory;
using Marshal.Data;
using Marshal.Data.Memory;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.AI;

namespace Marshal.Memory;

/// <summary>
/// Raw SQL on the keyword (<c>chunks_fts</c>) and vector (<c>chunk_vectors</c>, sqlite-vec) indexes. Commands join the
/// context's current transaction. Both tables carry the owner, and every query filters on it explicitly, because the
/// EF Core owner filters do not apply to raw SQL.
/// </summary>
internal static class MemoryIndexes
{
    public const string VectorTable = "chunk_vectors";

    /// <summary>The text a chunk is embedded from.</summary>
    public static string EmbeddingText(string title, string content) => $"{title}\n{content}";

    /// <summary>The model id recorded for the vector table.</summary>
    public static string ModelName(IEmbeddingGenerator<string, Embedding<float>> embeddings) =>
        embeddings.GetService<EmbeddingGeneratorMetadata>()?.DefaultModelId ?? "unknown";

    /// <summary>Owner value stored in the index tables.</summary>
    public static string OwnerKey(Abstractions.Identity.OwnerId owner) => owner.Value.ToString("D");

    /// <summary>
    /// Creates the vector table for <paramref name="dimensions"/> when there is none. A table built for another model or
    /// dimension is an error here; startup rebuilds it.
    /// </summary>
    public static async Task EnsureVectorTableAsync(MarshalDbContext db, IEmbeddingGenerator<string, Embedding<float>> embeddings, int dimensions, CancellationToken cancellationToken)
    {
        string model = ModelName(embeddings);
        (string Model, int Dimensions)? info = await ReadInfoAsync(db, cancellationToken);
        if (info is null)
        {
            await CreateVectorTableAsync(db, model, dimensions, cancellationToken);
            return;
        }

        if (info.Value.Dimensions != dimensions || info.Value.Model != model)
            throw new InvalidOperationException($"The memory vector index was built for {info.Value.Model} ({info.Value.Dimensions} dimensions), not {model} ({dimensions}); restart Marshal to re-embed.");
    }

    /// <summary>Drops and recreates the vector table for a model and dimension.</summary>
    public static async Task CreateVectorTableAsync(MarshalDbContext db, string model, int dimensions, CancellationToken cancellationToken)
    {
        await ExecuteAsync(db, $"DROP TABLE IF EXISTS {VectorTable}", cancellationToken);
        await ExecuteAsync(db, $"CREATE VIRTUAL TABLE {VectorTable} USING vec0(chunk_id TEXT PRIMARY KEY, owner_id TEXT PARTITION KEY, embedding float[{dimensions}] distance_metric=cosine)", cancellationToken);
        await ExecuteAsync(db, "INSERT OR REPLACE INTO memory_vector_index (id, model, dimensions) VALUES (1, $model, $dimensions)", cancellationToken,
            ("$model", model), ("$dimensions", dimensions));
    }

    /// <summary>The model and dimension the vector table was built with, or null when there is none.</summary>
    public static async Task<(string Model, int Dimensions)?> ReadInfoAsync(MarshalDbContext db, CancellationToken cancellationToken)
    {
        await using DbCommand command = Command(db, "SELECT model, dimensions FROM memory_vector_index WHERE id = 1");
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? (reader.GetString(0), reader.GetInt32(1)) : null;
    }

    /// <summary>Replaces the index entries of a live chunk.</summary>
    public static async Task PutAsync(MarshalDbContext db, Chunk chunk, float[] vector, CancellationToken cancellationToken)
    {
        await RemoveAsync(db, chunk.Id, cancellationToken);
        string owner = OwnerKey(chunk.OwnerId);
        await ExecuteAsync(db, "INSERT INTO chunks_fts (chunk_id, owner_id, title, content) VALUES ($id, $owner, $title, $content)", cancellationToken,
            ("$id", chunk.Id.Value), ("$owner", owner), ("$title", chunk.Title), ("$content", chunk.Content));
        await ExecuteAsync(db, $"INSERT INTO {VectorTable} (chunk_id, owner_id, embedding) VALUES ($id, $owner, $embedding)", cancellationToken,
            ("$id", chunk.Id.Value), ("$owner", owner), ("$embedding", ToBlob(vector)));
    }

    /// <summary>Adds the keyword entry of a chunk version.</summary>
    public static Task PutVersionAsync(MarshalDbContext db, ChunkVersion version, CancellationToken cancellationToken) =>
        ExecuteAsync(db, "INSERT INTO chunk_versions_fts (version_id, chunk_id, owner_id, content) VALUES ($version, $chunk, $owner, $content)", cancellationToken,
            ("$version", version.Id.ToString("D")), ("$chunk", version.ChunkId.Value), ("$owner", OwnerKey(version.OwnerId)), ("$content", version.Content));

    /// <summary>Removes the index entries of a chunk (the vector table may not exist yet).</summary>
    public static async Task RemoveAsync(MarshalDbContext db, ChunkId id, CancellationToken cancellationToken)
    {
        await ExecuteAsync(db, "DELETE FROM chunks_fts WHERE chunk_id = $id", cancellationToken, ("$id", id.Value));
        if (await ReadInfoAsync(db, cancellationToken) is not null)
            await ExecuteAsync(db, $"DELETE FROM {VectorTable} WHERE chunk_id = $id", cancellationToken, ("$id", id.Value));
    }

    /// <summary>Stored vectors of the given chunks (those without one are skipped).</summary>
    public static async Task<IReadOnlyList<float[]>> ReadVectorsAsync(MarshalDbContext db, IReadOnlyList<ChunkId> ids, CancellationToken cancellationToken)
    {
        var vectors = new List<float[]>();
        if (ids.Count == 0 || await ReadInfoAsync(db, cancellationToken) is null)
            return vectors;

        foreach (ChunkId id in ids)
        {
            await using DbCommand command = Command(db, $"SELECT embedding FROM {VectorTable} WHERE chunk_id = $id", ("$id", id.Value));
            if (await command.ExecuteScalarAsync(cancellationToken) is byte[] blob)
                vectors.Add(Vectors.FromBlob(blob));
        }

        return vectors;
    }

    /// <summary>Clears a project's stored centroid; it is recomputed on the next activation.</summary>
    public static Task InvalidateCentroidAsync(MarshalDbContext db, ProjectId? project, CancellationToken cancellationToken) =>
        project is { } id
            ? db.Projects.Where(candidate => candidate.Id == id).ExecuteUpdateAsync(update => update.SetProperty(candidate => candidate.Centroid, (byte[]?)null), cancellationToken)
            : Task.CompletedTask;

    /// <summary>float32 little-endian, the sqlite-vec blob format.</summary>
    public static byte[] ToBlob(ReadOnlySpan<float> vector) => MemoryMarshal.AsBytes(vector).ToArray();

    public static async Task ExecuteAsync(MarshalDbContext db, string sql, CancellationToken cancellationToken, params (string Name, object Value)[] parameters)
    {
        await using DbCommand command = Command(db, sql, parameters);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public static DbCommand Command(MarshalDbContext db, string sql, params (string Name, object Value)[] parameters)
    {
        DbConnection connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            db.Database.OpenConnection();

        DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        foreach ((string name, object value) in parameters)
            command.Parameters.Add(new SqliteParameter(name, value));

        return command;
    }
}
