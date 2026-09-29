namespace Marshal.Data.Notes;

/// <summary>
/// Notes index tables EF Core cannot model, separate from memory: the FTS5 keyword index and the vector index
/// bookkeeping. The migration creates them with the same statements; <see cref="Create"/> serves databases made by
/// <c>EnsureCreated</c> (tests). The <c>note_vectors</c> sqlite-vec table is created at runtime, because its dimension
/// comes from the configured embedding model.
/// </summary>
public static class NotesSchema
{
    /// <summary>Keyword index over note sections; one row per <see cref="NoteIndexEntry"/>.</summary>
    public const string CreateKeywordIndex =
        "CREATE VIRTUAL TABLE IF NOT EXISTS notes_fts USING fts5(entry_id UNINDEXED, owner_id UNINDEXED, path, heading, text, tokenize = 'unicode61 remove_diacritics 2')";

    /// <summary>Embedding model and dimension the notes vector table was built with (single row, id 1).</summary>
    public const string CreateVectorIndexInfo =
        "CREATE TABLE IF NOT EXISTS notes_vector_index (id INTEGER NOT NULL PRIMARY KEY CHECK (id = 1), model TEXT NOT NULL, dimensions INTEGER NOT NULL)";

    /// <summary>All statements, idempotent.</summary>
    public static IReadOnlyList<string> Create { get; } = [CreateKeywordIndex, CreateVectorIndexInfo];
}
