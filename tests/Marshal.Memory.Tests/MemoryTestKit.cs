using System.Data.Common;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Memory;
using Marshal.Data;
using Marshal.Data.Memory;
using Marshal.Memory.Retrieval;
using Marshal.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Marshal.Memory.Tests;

/// <summary>
/// Builds memory services over a <see cref="TestDatabase"/> and inspects the raw indexes.
/// </summary>
internal static class MemoryTestKit
{
    public static SqliteMemoryStore Store(TestDatabase database, IEmbeddingGenerator<string, Embedding<float>> embeddings, FakeTimeProvider time) =>
        new(database.Factory, embeddings, database.Accessor, time);

    public static HybridMemoryRetriever Retriever(TestDatabase database, IEmbeddingGenerator<string, Embedding<float>> embeddings, FakeTimeProvider time, MemoryOptions? options = null)
    {
        IOptions<MemoryOptions> memoryOptions = Microsoft.Extensions.Options.Options.Create(options ?? new MemoryOptions());
        return new(database.Factory, embeddings, database.Accessor, new ChunkScorer(memoryOptions), memoryOptions, time);
    }

    public static async Task<ProjectId> AddProjectAsync(TestDatabase database, string name, string description = "")
    {
        await using MarshalDbContext db = database.CreateContext();
        var project = new Project
        {
            Id = ProjectId.New(),
            OwnerId = database.Accessor.Current!.OwnerId,
            Name = name,
            Description = description,
            CreatedAt = DateTimeOffset.UnixEpoch,
        };
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        return project.Id;
    }

    /// <summary>Chunk ids whose keyword index entry matches <paramref name="phrase"/>, for the current owner.</summary>
    public static async Task<List<ChunkId>> KeywordHitsAsync(TestDatabase database, string phrase)
    {
        OwnerId owner = database.Accessor.Current!.OwnerId;
        return await QueryAsync(database, "SELECT chunk_id FROM chunks_fts WHERE chunks_fts MATCH $phrase AND owner_id = $owner", reader => ChunkId.Parse(reader.GetString(0), null),
            ("$phrase", $"\"{phrase}\""), ("$owner", owner.Value.ToString()));
    }

    /// <summary>The stored vector of a chunk, or null when it has none.</summary>
    public static async Task<float[]?> StoredVectorAsync(TestDatabase database, ChunkId id)
    {
        List<float[]> vectors = await QueryAsync(database, "SELECT embedding FROM chunk_vectors WHERE chunk_id = $id",
            reader => System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>((byte[])reader.GetValue(0)).ToArray(), ("$id", id.Value));
        return vectors.SingleOrDefault();
    }

    public static async Task<float[]> EmbedAsync(IEmbeddingGenerator<string, Embedding<float>> embeddings, string text) =>
        (await embeddings.GenerateAsync([text])).Single().Vector.ToArray();

    private static async Task<List<T>> QueryAsync<T>(TestDatabase database, string sql, Func<DbDataReader, T> read, params (string Name, object Value)[] parameters)
    {
        await using MarshalDbContext db = database.CreateContext();
        DbConnection connection = db.Database.GetDbConnection();
        await using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        foreach ((string name, object value) in parameters)
            command.Parameters.Add(new SqliteParameter(name, value));

        var results = new List<T>();
        try
        {
            await using DbDataReader reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                results.Add(read(reader));
        }
        catch (SqliteException exception) when (exception.Message.Contains("no such table", StringComparison.Ordinal))
        {
        }

        return results;
    }
}
