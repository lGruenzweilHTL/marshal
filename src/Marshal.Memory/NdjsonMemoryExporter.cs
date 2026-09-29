using System.Globalization;
using System.Text;
using System.Text.Json;
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
/// <see cref="IMemoryExporter"/> as NDJSON: projects (needed by project chunks), then chunks, versions and edges, each
/// sorted, with a fixed field order and no embeddings. Import recomputes chunk ids for the importing owner (the same
/// ids for the same owner), keeps timestamps, pins and authors, and re-embeds the imported live chunks.
/// </summary>
public sealed class NdjsonMemoryExporter(
    IDbContextFactory<MarshalDbContext> contexts,
    IEmbeddingGenerator<string, Embedding<float>> embeddings,
    IExecutionContextAccessor executionContext) : IMemoryExporter
{
    private static readonly JsonWriterOptions _writerOptions = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public async Task ExportAsync(Stream output, CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        List<Project> projects = await db.Projects.AsNoTracking().ToListAsync(cancellationToken);
        List<Chunk> chunks = await db.Chunks.AsNoTracking().ToListAsync(cancellationToken);
        List<ChunkVersion> versions = await db.ChunkVersions.AsNoTracking().ToListAsync(cancellationToken);
        List<ChunkEdge> edges = await db.ChunkEdges.AsNoTracking().ToListAsync(cancellationToken);

        foreach (Project project in projects.OrderBy(project => project.Id.Value))
        {
            await WriteLineAsync(output, writer =>
            {
                writer.WriteString("kind", "project");
                writer.WriteString("id", project.Id.ToString());
                writer.WriteString("name", project.Name);
                writer.WriteString("description", project.Description);
                writer.WriteString("createdAt", Time(project.CreatedAt));
                WriteNullable(writer, "deletedAt", project.DeletedAt is { } deleted ? Time(deleted) : null);
            }, cancellationToken);
        }

        foreach (Chunk chunk in chunks.OrderBy(chunk => chunk.Id.Value, StringComparer.Ordinal))
        {
            await WriteLineAsync(output, writer =>
            {
                writer.WriteString("kind", "chunk");
                writer.WriteString("id", chunk.Id.Value);
                writer.WriteString("scope", Name(chunk.Scope));
                WriteNullable(writer, "projectId", chunk.ProjectId?.ToString());
                writer.WriteString("type", Name(chunk.Type));
                writer.WriteString("title", chunk.Title);
                writer.WriteString("content", chunk.Content);
                writer.WriteNumber("importance", chunk.Importance);
                writer.WriteBoolean("pinned", chunk.Pinned);
                WriteNullable(writer, "sourceRef", chunk.SourceRef);
                writer.WriteString("createdBy", Name(chunk.CreatedBy));
                writer.WriteString("createdAt", Time(chunk.CreatedAt));
                writer.WriteString("updatedAt", Time(chunk.UpdatedAt));
                WriteNullable(writer, "deletedAt", chunk.DeletedAt is { } deleted ? Time(deleted) : null);
            }, cancellationToken);
        }

        foreach (ChunkVersion version in versions.OrderBy(version => version.ChunkId.Value, StringComparer.Ordinal).ThenBy(version => version.ReplacedAt).ThenBy(version => version.Id))
        {
            await WriteLineAsync(output, writer =>
            {
                writer.WriteString("kind", "version");
                writer.WriteString("id", version.Id.ToString());
                writer.WriteString("chunkId", version.ChunkId.Value);
                writer.WriteString("content", version.Content);
                writer.WriteNumber("importance", version.Importance);
                WriteNullable(writer, "sourceRef", version.SourceRef);
                writer.WriteString("replacedAt", Time(version.ReplacedAt));
            }, cancellationToken);
        }

        foreach (ChunkEdge edge in edges.OrderBy(edge => edge.FromId.Value, StringComparer.Ordinal).ThenBy(edge => edge.ToId.Value, StringComparer.Ordinal).ThenBy(edge => edge.Kind))
        {
            await WriteLineAsync(output, writer =>
            {
                writer.WriteString("kind", "edge");
                writer.WriteString("from", edge.FromId.Value);
                writer.WriteString("to", edge.ToId.Value);
                writer.WriteString("edgeKind", Name(edge.Kind));
                writer.WriteString("createdAt", Time(edge.CreatedAt));
            }, cancellationToken);
        }
    }

    public async Task<Result<MemoryImportReport>> ImportAsync(Stream input, CancellationToken cancellationToken)
    {
        OwnerId owner = executionContext.Current?.OwnerId ?? throw new InvalidOperationException("Memory accessed without an execution context.");
        Result<Parsed> parsed = await ParseAsync(input, owner, cancellationToken);
        if (parsed.IsFailure)
            return parsed.Error;

        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        HashSet<ProjectId> projectIds = [.. await db.Projects.Select(project => project.Id).ToListAsync(cancellationToken)];
        HashSet<ChunkId> chunkIds = [.. await db.Chunks.Select(chunk => chunk.Id).ToListAsync(cancellationToken)];
        HashSet<Guid> versionIds = [.. await db.ChunkVersions.Select(version => version.Id).ToListAsync(cancellationToken)];
        var edgeKeys = (await db.ChunkEdges.Select(edge => new { edge.FromId, edge.ToId, edge.Kind }).ToListAsync(cancellationToken))
            .Select(edge => (edge.FromId, edge.ToId, edge.Kind)).ToHashSet();

        List<Project> projects = [.. parsed.Value.Projects.Where(project => !projectIds.Contains(project.Id))];
        List<Chunk> chunks = [.. parsed.Value.Chunks.Where(chunk => !chunkIds.Contains(chunk.Id))];
        List<ChunkVersion> versions = [.. parsed.Value.Versions.Where(version => !versionIds.Contains(version.Id))];
        List<ChunkEdge> edges = [.. parsed.Value.Edges.Where(edge => !edgeKeys.Contains((edge.FromId, edge.ToId, edge.Kind)))];
        int skipped = parsed.Value.Count - projects.Count - chunks.Count - versions.Count - edges.Count;

        List<Chunk> live = [.. chunks.Where(chunk => chunk.DeletedAt is null)];
        GeneratedEmbeddings<Embedding<float>> vectors = live.Count == 0
            ? []
            : await embeddings.GenerateAsync([.. live.Select(chunk => MemoryIndexes.EmbeddingText(chunk.Title, chunk.Content))], cancellationToken: cancellationToken);

        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        db.Projects.AddRange(projects);
        db.Chunks.AddRange(chunks);
        db.ChunkVersions.AddRange(versions);
        db.ChunkEdges.AddRange(edges);
        await db.SaveChangesAsync(cancellationToken);
        for (int index = 0; index < live.Count; index++)
        {
            float[] vector = vectors[index].Vector.ToArray();
            await MemoryIndexes.EnsureVectorTableAsync(db, embeddings, vector.Length, cancellationToken);
            await MemoryIndexes.PutAsync(db, live[index], vector, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return new MemoryImportReport(projects.Count, chunks.Count, versions.Count, edges.Count, skipped);
    }

    private static async Task<Result<Parsed>> ParseAsync(Stream input, OwnerId owner, CancellationToken cancellationToken)
    {
        var parsed = new Parsed();
        var ids = new Dictionary<string, ChunkId>(StringComparer.Ordinal);
        using var reader = new StreamReader(input, Encoding.UTF8);
        int number = 0;
        var pending = new List<(int Number, JsonElement Line)>();
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            number++;
            if (string.IsNullOrWhiteSpace(line))
                continue;

            try
            {
                pending.Add((number, JsonDocument.Parse(line).RootElement.Clone()));
            }
            catch (JsonException exception)
            {
                return Invalid(number, exception.Message);
            }
        }

        // Chunks first, so versions and edges can be mapped to the importing owner's ids.
        foreach ((int lineNumber, JsonElement line) in pending.OrderBy(entry => Kind(entry.Line) switch { "project" => 0, "chunk" => 1, _ => 2 }))
        {
            try
            {
                switch (Kind(line))
                {
                    case "project":
                        parsed.Projects.Add(new Project
                        {
                            Id = ProjectId.Parse(Text(line, "id"), null),
                            OwnerId = owner,
                            Name = Text(line, "name"),
                            Description = Text(line, "description"),
                            CreatedAt = ParseTime(Text(line, "createdAt")),
                            DeletedAt = OptionalTime(line, "deletedAt"),
                        });
                        break;

                    case "chunk":
                        ChunkScope scope = Parse<ChunkScope>(Text(line, "scope"));
                        ProjectId? project = Optional(line, "projectId") is { } projectText ? ProjectId.Parse(projectText, null) : null;
                        ChunkType type = Parse<ChunkType>(Text(line, "type"));
                        string title = Text(line, "title");
                        ChunkId id = ChunkIds.Compute(owner, scope, project, type, title);
                        ids[Text(line, "id")] = id;
                        parsed.Chunks.Add(new Chunk
                        {
                            Id = id,
                            OwnerId = owner,
                            Scope = scope,
                            ProjectId = project,
                            Type = type,
                            Title = title,
                            Content = Text(line, "content"),
                            Importance = line.GetProperty("importance").GetInt32(),
                            Pinned = line.GetProperty("pinned").GetBoolean(),
                            SourceRef = Optional(line, "sourceRef"),
                            CreatedBy = Parse<ChunkAuthor>(Text(line, "createdBy")),
                            CreatedAt = ParseTime(Text(line, "createdAt")),
                            UpdatedAt = ParseTime(Text(line, "updatedAt")),
                            DeletedAt = OptionalTime(line, "deletedAt"),
                        });
                        break;

                    case "version":
                        parsed.Versions.Add(new ChunkVersion
                        {
                            Id = Guid.Parse(Text(line, "id")),
                            OwnerId = owner,
                            ChunkId = Mapped(ids, Text(line, "chunkId")),
                            Content = Text(line, "content"),
                            Importance = line.GetProperty("importance").GetInt32(),
                            SourceRef = Optional(line, "sourceRef"),
                            ReplacedAt = ParseTime(Text(line, "replacedAt")),
                        });
                        break;

                    case "edge":
                        parsed.Edges.Add(new ChunkEdge
                        {
                            FromId = Mapped(ids, Text(line, "from")),
                            ToId = Mapped(ids, Text(line, "to")),
                            Kind = Parse<ChunkEdgeKind>(Text(line, "edgeKind")),
                            OwnerId = owner,
                            CreatedAt = ParseTime(Text(line, "createdAt")),
                        });
                        break;

                    default:
                        return Invalid(lineNumber, $"unknown kind '{Kind(line)}'.");
                }
            }
            catch (Exception exception) when (exception is KeyNotFoundException or FormatException or InvalidOperationException or ArgumentException)
            {
                return Invalid(lineNumber, exception.Message);
            }
        }

        return parsed;
    }

    private static string? Kind(JsonElement line) =>
        line.ValueKind == JsonValueKind.Object && line.TryGetProperty("kind", out JsonElement kind) ? kind.GetString() : null;

    private static string Text(JsonElement line, string name) =>
        line.GetProperty(name).GetString() ?? throw new FormatException($"'{name}' must not be null.");

    private static string? Optional(JsonElement line, string name) =>
        line.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static DateTimeOffset? OptionalTime(JsonElement line, string name) =>
        Optional(line, name) is { } value ? ParseTime(value) : null;

    private static ChunkId Mapped(Dictionary<string, ChunkId> ids, string exported) =>
        ids.TryGetValue(exported, out ChunkId id) ? id : throw new FormatException($"chunk {exported} is not part of the export.");

    private static T Parse<T>(string value)
        where T : struct, Enum
    {
        foreach (T candidate in Enum.GetValues<T>())
        {
            if (Name(candidate) == value)
                return candidate;
        }

        throw new FormatException($"'{value}' is not a valid {typeof(T).Name}.");
    }

    private static string Name<T>(T value)
        where T : struct, Enum => JsonNamingPolicy.SnakeCaseLower.ConvertName(value.ToString());

    private static string Time(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseTime(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static void WriteNullable(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is null)
            writer.WriteNull(name);
        else
            writer.WriteString(name, value);
    }

    private static async Task WriteLineAsync(Stream output, Action<Utf8JsonWriter> write, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await using (var writer = new Utf8JsonWriter(buffer, _writerOptions))
        {
            writer.WriteStartObject();
            write(writer);
            writer.WriteEndObject();
        }

        buffer.WriteByte((byte)'\n');
        await output.WriteAsync(buffer.ToArray(), cancellationToken);
    }

    private static Error Invalid(int line, string message) =>
        new("memory.invalid_import", $"Import line {line}: {message}", ErrorType.Validation);

    private sealed class Parsed
    {
        public List<Project> Projects { get; } = [];

        public List<Chunk> Chunks { get; } = [];

        public List<ChunkVersion> Versions { get; } = [];

        public List<ChunkEdge> Edges { get; } = [];

        public int Count => Projects.Count + Chunks.Count + Versions.Count + Edges.Count;
    }
}
