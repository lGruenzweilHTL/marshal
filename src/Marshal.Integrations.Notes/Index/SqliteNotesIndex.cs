using System.Data.Common;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Notes;
using Marshal.Data;
using Marshal.Data.Notes;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.AI;

namespace Marshal.Integrations.Notes.Index;

/// <summary>
/// The notes index in SQLite, separate from memory: <see cref="NoteIndexEntry"/> rows, an FTS5 keyword index
/// (<c>notes_fts</c>) and a sqlite-vec vector table (<c>note_vectors</c>). A file is re-indexed when its SHA-256
/// changes; entries of removed files are dropped. A different embedding model empties the whole index, which the
/// next update rebuilds. Raw SQL filters on the owner explicitly, because EF Core's owner filters do not apply to it.
/// </summary>
public sealed partial class SqliteNotesIndex(
    IDbContextFactory<MarshalDbContext> contexts,
    IEmbeddingGenerator<string, Embedding<float>> embeddings,
    IExecutionContextAccessor executionContext,
    TimeProvider time) : INotesIndex
{
    private const string VectorTable = "note_vectors";
    private const int SeedCount = 20;

    // Keyword hits rank just below an exact vector match, relative to the best bm25 score of the query.
    private const double KeywordWeight = 0.9;

    public async Task<Result<NotesIndexUpdate>> UpdateAsync(INotesSource source, CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<NoteFile>> files = await source.ListAsync(null, cancellationToken);
        if (files.IsFailure)
            return files.Error;

        OwnerId owner = Owner;
        string model = embeddings.GetService<EmbeddingGeneratorMetadata>()?.DefaultModelId ?? "unknown";
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        if (await ReadInfoAsync(db, cancellationToken) is { } info && info.Model != model)
            await ResetAsync(db, cancellationToken);

        var indexed = (await db.NoteIndexEntries.AsNoTracking()
                .Select(entry => new { entry.Path, entry.FileHash })
                .Distinct()
                .ToListAsync(cancellationToken))
            .GroupBy(entry => entry.Path, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().FileHash, StringComparer.Ordinal);

        var reindexed = new List<string>();
        foreach (NoteFile file in files.Value)
        {
            Result<string> text = await source.ReadAsync(file.Path, cancellationToken);
            if (text.IsFailure)
                continue;

            string hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.Value)));
            if (indexed.GetValueOrDefault(file.Path) == hash)
                continue;

            await IndexFileAsync(db, owner, model, file.Path, hash, NoteSections.Split(file.Path, text.Value), cancellationToken);
            reindexed.Add(file.Path);
        }

        var present = new HashSet<string>(files.Value.Select(file => file.Path), StringComparer.Ordinal);
        List<string> removed = [.. indexed.Keys.Where(path => !present.Contains(path)).Order(StringComparer.Ordinal)];
        foreach (string path in removed)
        {
            await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            await RemoveFileAsync(db, owner, path, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        return new NotesIndexUpdate(reindexed, removed);
    }

    public async Task<IReadOnlyList<NoteHit>> SearchAsync(string query, int limit, CancellationToken cancellationToken)
    {
        string owner = OwnerKey(Owner);
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        var found = new Dictionary<Guid, double>();
        if (await ReadInfoAsync(db, cancellationToken) is { } info)
        {
            ReadOnlyMemory<float> vector = (await embeddings.GenerateAsync([query], cancellationToken: cancellationToken)).Single().Vector;
            if (vector.Length == info.Dimensions)
            {
                await using DbCommand command = Command(db,
                    $"SELECT entry_id, distance FROM {VectorTable} WHERE embedding MATCH $embedding AND k = $k AND owner_id = $owner",
                    ("$embedding", ToBlob(vector.Span)), ("$k", SeedCount), ("$owner", owner));
                await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                    Add(found, Guid.Parse(reader.GetString(0)), Math.Clamp(1 - reader.GetDouble(1), 0, 1));
            }
        }

        foreach ((Guid id, double similarity) in await KeywordHitsAsync(db, owner, query, cancellationToken))
            Add(found, id, similarity);

        List<Guid> ids = [.. found.Keys];
        List<NoteIndexEntry> entries = await db.NoteIndexEntries.AsNoTracking().Where(entry => ids.Contains(entry.Id)).ToListAsync(cancellationToken);
        return [.. entries
            .Select(entry => new NoteHit(entry.Path, entry.Heading, entry.Text, found[entry.Id]))
            .OrderByDescending(hit => hit.Score)
            .ThenBy(hit => hit.Path, StringComparer.Ordinal)
            .Take(limit)];
    }

    private async Task IndexFileAsync(
        MarshalDbContext db, OwnerId owner, string model, string path, string hash, IReadOnlyList<NoteSection> sections, CancellationToken cancellationToken)
    {
        List<ReadOnlyMemory<float>> vectors = sections.Count == 0
            ? []
            : [.. (await embeddings.GenerateAsync(sections.Select(section => EmbeddingText(path, section)), cancellationToken: cancellationToken)).Select(embedding => embedding.Vector)];
        if (vectors.Count > 0)
            await EnsureVectorTableAsync(db, model, vectors[0].Length, cancellationToken);

        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await RemoveFileAsync(db, owner, path, cancellationToken);
        DateTimeOffset now = time.GetUtcNow();
        string ownerKey = OwnerKey(owner);
        for (int position = 0; position < sections.Count; position++)
        {
            var entry = new NoteIndexEntry
            {
                Id = Guid.CreateVersion7(),
                OwnerId = owner,
                Path = path,
                Heading = sections[position].Heading,
                Position = position,
                Text = sections[position].Text,
                FileHash = hash,
                IndexedAt = now,
            };
            db.NoteIndexEntries.Add(entry);
            await ExecuteAsync(db, "INSERT INTO notes_fts (entry_id, owner_id, path, heading, text) VALUES ($id, $owner, $path, $heading, $text)", cancellationToken,
                ("$id", entry.Id.ToString("D")), ("$owner", ownerKey), ("$path", path), ("$heading", entry.Heading), ("$text", entry.Text));
            await ExecuteAsync(db, $"INSERT INTO {VectorTable} (entry_id, owner_id, embedding) VALUES ($id, $owner, $embedding)", cancellationToken,
                ("$id", entry.Id.ToString("D")), ("$owner", ownerKey), ("$embedding", ToBlob(vectors[position].Span)));
        }

        // A file without sections keeps one marker row, so an unchanged empty file is not re-read as new every time.
        if (sections.Count == 0)
        {
            db.NoteIndexEntries.Add(new NoteIndexEntry
            {
                Id = Guid.CreateVersion7(),
                OwnerId = owner,
                Path = path,
                Heading = "",
                Position = 0,
                Text = "",
                FileHash = hash,
                IndexedAt = now,
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task RemoveFileAsync(MarshalDbContext db, OwnerId owner, string path, CancellationToken cancellationToken)
    {
        List<Guid> ids = await db.NoteIndexEntries.Where(entry => entry.Path == path).Select(entry => entry.Id).ToListAsync(cancellationToken);
        bool vectors = await ReadInfoAsync(db, cancellationToken) is not null;
        foreach (Guid id in ids)
        {
            await ExecuteAsync(db, "DELETE FROM notes_fts WHERE entry_id = $id AND owner_id = $owner", cancellationToken, ("$id", id.ToString("D")), ("$owner", OwnerKey(owner)));
            if (vectors)
                await ExecuteAsync(db, $"DELETE FROM {VectorTable} WHERE entry_id = $id", cancellationToken, ("$id", id.ToString("D")));
        }

        await db.NoteIndexEntries.Where(entry => entry.Path == path).ExecuteDeleteAsync(cancellationToken);
    }

    private static async Task EnsureVectorTableAsync(MarshalDbContext db, string model, int dimensions, CancellationToken cancellationToken)
    {
        if (await ReadInfoAsync(db, cancellationToken) is { } info)
        {
            if (info.Dimensions == dimensions)
                return;

            await ResetAsync(db, cancellationToken);
        }

        await ExecuteAsync(db, $"CREATE VIRTUAL TABLE {VectorTable} USING vec0(entry_id TEXT PRIMARY KEY, owner_id TEXT PARTITION KEY, embedding float[{dimensions}] distance_metric=cosine)", cancellationToken);
        await ExecuteAsync(db, "INSERT OR REPLACE INTO notes_vector_index (id, model, dimensions) VALUES (1, $model, $dimensions)", cancellationToken,
            ("$model", model), ("$dimensions", dimensions));
    }

    // Another embedding model: every owner's vectors are useless, so the whole derived index goes.
    private static async Task ResetAsync(MarshalDbContext db, CancellationToken cancellationToken)
    {
        await ExecuteAsync(db, $"DROP TABLE IF EXISTS {VectorTable}", cancellationToken);
        await ExecuteAsync(db, "DELETE FROM notes_vector_index", cancellationToken);
        await ExecuteAsync(db, "DELETE FROM notes_fts", cancellationToken);
        await db.NoteIndexEntries.IgnoreQueryFilters().ExecuteDeleteAsync(cancellationToken);
    }

    private static async Task<(string Model, int Dimensions)?> ReadInfoAsync(MarshalDbContext db, CancellationToken cancellationToken)
    {
        await using DbCommand command = Command(db, "SELECT model, dimensions FROM notes_vector_index WHERE id = 1");
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? (reader.GetString(0), reader.GetInt32(1)) : null;
    }

    private static async Task<List<(Guid Id, double Similarity)>> KeywordHitsAsync(MarshalDbContext db, string owner, string text, CancellationToken cancellationToken)
    {
        var hits = new List<(Guid, double)>();
        string[] terms = [.. Words().Matches(text).Select(match => match.Value).Where(word => word.Length > 1).Distinct(StringComparer.OrdinalIgnoreCase)];
        if (terms.Length == 0)
            return hits;

        // Every term is quoted, so FTS5 query syntax in the query is taken literally.
        string match = string.Join(" OR ", terms.Select(term => $"\"{term}\""));
        var ranked = new List<(Guid Id, double Rank)>();
        await using (DbCommand command = Command(db,
            "SELECT entry_id, bm25(notes_fts) AS rank FROM notes_fts WHERE notes_fts MATCH $match AND owner_id = $owner ORDER BY rank LIMIT $k",
            ("$match", match), ("$owner", owner), ("$k", SeedCount)))
        await using (DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
                ranked.Add((Guid.Parse(reader.GetString(0)), reader.GetDouble(1)));
        }

        // bm25 is negative, lower is better; the best hit gets the full keyword weight.
        double best = ranked.Count > 0 ? ranked.Min(hit => hit.Rank) : 0;
        foreach ((Guid id, double rank) in ranked)
            hits.Add((id, best < 0 ? KeywordWeight * rank / best : KeywordWeight));

        return hits;
    }

    private static void Add(Dictionary<Guid, double> found, Guid id, double similarity)
    {
        if (!found.TryGetValue(id, out double existing) || existing < similarity)
            found[id] = similarity;
    }

    private OwnerId Owner => executionContext.Current?.OwnerId
        ?? throw new InvalidOperationException("The notes index was used without an execution context.");

    private static string OwnerKey(OwnerId owner) => owner.Value.ToString("D");

    private static string EmbeddingText(string path, NoteSection section) => $"{path}\n{section.Heading}\n{section.Text}";

    private static byte[] ToBlob(ReadOnlySpan<float> vector) => MemoryMarshal.AsBytes(vector).ToArray();

    private static async Task ExecuteAsync(MarshalDbContext db, string sql, CancellationToken cancellationToken, params (string Name, object Value)[] parameters)
    {
        await using DbCommand command = Command(db, sql, parameters);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static DbCommand Command(MarshalDbContext db, string sql, params (string Name, object Value)[] parameters)
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

    [GeneratedRegex(@"[\p{L}\p{N}]+", RegexOptions.CultureInvariant)]
    private static partial Regex Words();
}
