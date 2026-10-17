using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Notes;
using Marshal.Abstractions.Tools;

namespace Marshal.Integrations.Notes.Tools;

/// <summary>
/// Model-facing notes tools. Notes reach a run only through these calls, never as automatic context. Their content is
/// untrusted (taint source <c>notes</c>) and sensitive: the run record keeps only the note paths.
/// </summary>
internal static class NotesTools
{
    private const string Taint = "notes";
    private const int MaxSearchLimit = 20;

    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web) { Encoder = JavaScriptEncoder.Create(UnicodeRanges.All) };

    public static IReadOnlyList<ITool> Create(INotesSource source, INotesIndex index)
    {
        var context = new NotesToolContext(source, index);
        return
        [
            new NotesTool(context, "list", "Lists the user's Markdown notes, optionally in one folder.", """
                {"type":"object","properties":{"folder":{"type":"string","description":"Folder path inside the notes, e.g. projects."}}}
                """, ListAsync),
            new NotesTool(context, "read", "Reads one note.", """
                {"type":"object","properties":{"path":{"type":"string","description":"Note path from notes_list or notes_search, e.g. projects/marshal.md."}},"required":["path"]}
                """, ReadAsync),
            new NotesTool(context, "search", "Searches the notes by meaning and keywords; returns matching sections.", """
                {"type":"object","properties":{
                  "query":{"type":"string"},
                  "limit":{"type":"integer","minimum":1,"maximum":20}},"required":["query"]}
                """, SearchAsync),
        ];
    }

    private static async Task<Result<ToolOutput>> ListAsync(NotesToolContext context, JsonElement arguments, CancellationToken ct)
    {
        Result<IReadOnlyList<NoteFile>> files = await context.Source.ListAsync(Text(arguments, "folder"), ct);
        return files.Map(list => new ToolOutput(
            JsonSerializer.Serialize(list.Take(500).Select(file => new { file.Path, file.Size, modified = file.ModifiedAt }), _json),
            list.Count > 500 ? $"{list.Count} notes" : null));
    }

    private static async Task<Result<ToolOutput>> ReadAsync(NotesToolContext context, JsonElement arguments, CancellationToken ct)
    {
        if (Text(arguments, "path") is not { } path)
            return new Error("notes.invalid_arguments", "path is required.", ErrorType.Validation);

        Result<string> text = await context.Source.ReadAsync(path, ct);
        return text.Map(content => new ToolOutput(content, path));
    }

    private static async Task<Result<ToolOutput>> SearchAsync(NotesToolContext context, JsonElement arguments, CancellationToken ct)
    {
        if (Text(arguments, "query") is not { } query)
            return new Error("notes.invalid_arguments", "query is required.", ErrorType.Validation);

        int limit = Math.Clamp(arguments.TryGetProperty("limit", out JsonElement value) && value.TryGetInt32(out int requested) ? requested : 5, 1, MaxSearchLimit);
        IReadOnlyList<NoteHit> hits = await context.Index.SearchAsync(query, limit, ct);
        return new ToolOutput(
            JsonSerializer.Serialize(hits.Select(hit => new { hit.Path, hit.Heading, hit.Text, score = Math.Round(hit.Score, 2) }), _json),
            string.Join(',', hits.Select(hit => $"{hit.Path}#{hit.Heading}")));
    }

    private static string? Text(JsonElement arguments, string name) =>
        arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text ? text.Trim() : null;

    private sealed record NotesToolContext(INotesSource Source, INotesIndex Index);

    private sealed class NotesTool(
        NotesToolContext context,
        string name,
        string description,
        string schema,
        Func<NotesToolContext, JsonElement, CancellationToken, Task<Result<ToolOutput>>> run) : ITool
    {
        public ToolDescriptor Descriptor { get; } = new()
        {
            Id = new ToolId("notes", name),
            Description = description,
            ParametersSchema = JsonDocument.Parse(schema).RootElement.Clone(),
            DefaultTier = RiskTier.Safe,
            UntrustedOutput = true,
            TaintKind = Taint,
            Sensitive = true,
            Fillers = name == "search" ? ToolFillers.Lines("Ich sehe in deinen Notizen nach.", "Let me check your notes.") : ToolFillers.None,
        };

        public ValueTask<bool> IsAvailableAsync(MarshalExecutionContext executionContext, CancellationToken cancellationToken) =>
            ValueTask.FromResult(context.Source.IsConfigured);

        public Task<Result<ToolOutput>> InvokeAsync(ToolInvocation invocation, CancellationToken cancellationToken) =>
            run(context, invocation.Arguments.ValueKind == JsonValueKind.Object ? invocation.Arguments : JsonDocument.Parse("{}").RootElement, cancellationToken);
    }
}
