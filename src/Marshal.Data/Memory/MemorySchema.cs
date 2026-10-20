namespace Marshal.Data.Memory;

/// <summary>
/// Memory tables EF Core cannot model: the FTS5 keyword index and the vector index bookkeeping. The migration
/// creates them with the same statements; <see cref="Create"/> serves databases made by <c>EnsureCreated</c> (tests).
/// The <c>chunk_vectors</c> sqlite-vec table itself is created at runtime, because its dimension comes from the
/// configured embedding model.
/// </summary>
public static class MemorySchema
{
    /// <summary>Keyword index over chunk titles and content; one row per live chunk.</summary>
    public const string CreateKeywordIndex =
        "CREATE VIRTUAL TABLE IF NOT EXISTS chunks_fts USING fts5(chunk_id UNINDEXED, owner_id UNINDEXED, title, content, tokenize = 'unicode61 remove_diacritics 2')";

    /// <summary>Keyword index over the content of chunk versions, for searches at a past time; one row per version.</summary>
    public const string CreateVersionKeywordIndex =
        "CREATE VIRTUAL TABLE IF NOT EXISTS chunk_versions_fts USING fts5(version_id UNINDEXED, chunk_id UNINDEXED, owner_id UNINDEXED, content, tokenize = 'unicode61 remove_diacritics 2')";

    /// <summary>Embedding model and dimension the vector table was built with (single row, id 1).</summary>
    public const string CreateVectorIndexInfo =
        "CREATE TABLE IF NOT EXISTS memory_vector_index (id INTEGER NOT NULL PRIMARY KEY CHECK (id = 1), model TEXT NOT NULL, dimensions INTEGER NOT NULL)";

    /// <summary>All statements, idempotent.</summary>
    public static IReadOnlyList<string> Create { get; } = [CreateKeywordIndex, CreateVersionKeywordIndex, CreateVectorIndexInfo];
}
