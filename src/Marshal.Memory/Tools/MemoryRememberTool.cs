using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Memory;
using Marshal.Abstractions.Tools;
using Marshal.Memory.Projects;

namespace Marshal.Memory.Tools;

/// <summary>
/// <c>memory.remember(type, title, content, importance?, scope?, project?, sourceRef?)</c>: writes a chunk through its
/// deterministic id, updating it in place when it exists. It cannot pin. Flagged <see cref="ToolDescriptor.Persists"/>,
/// so the guard escalates it to confirm in tainted runs.
/// </summary>
public sealed class MemoryRememberTool(IMemoryStore store, IMemoryProjects projects) : ITool
{
    public ToolDescriptor Descriptor { get; } = new()
    {
        Id = new ToolId("memory", "remember"),
        Description = "Stores something in long-term memory when the user asks you to remember it. The same type and title update the existing entry. Write title and content in English.",
        ParametersSchema = JsonDocument.Parse("""
            {"type":"object","properties":{
              "type":{"type":"string","enum":["fact","preference","event","procedure","person","device"]},
              "title":{"type":"string","description":"Short English title; identifies the entry."},
              "content":{"type":"string","description":"The knowledge itself, in English."},
              "importance":{"type":"integer","minimum":1,"maximum":5,"description":"1 trivia to 5 essential (default 3)."},
              "scope":{"type":"string","enum":["general","project"],"description":"Default: project when a project is given, else general."},
              "project":{"type":"string","description":"Project name or alias for project-scoped entries."},
              "sourceRef":{"type":"string","description":"Where the knowledge comes from, e.g. a note path."}},
             "required":["type","title","content"]}
            """).RootElement.Clone(),
        DefaultTier = RiskTier.Safe,
        Persists = true,
    };

    public string Summarize(JsonElement arguments, string language) =>
        $"remember {ToolArguments.Text(arguments, "title")}: {ToolArguments.Text(arguments, "content")}";

    public async Task<Result<ToolOutput>> InvokeAsync(ToolInvocation invocation, CancellationToken cancellationToken)
    {
        JsonElement arguments = invocation.Arguments;
        string? typeText = ToolArguments.Text(arguments, "type");
        if (typeText is null || !ToolArguments.TryEnum(typeText, out ChunkType type))
            return ToolArguments.Invalid("type must be one of fact, preference, event, procedure, person, device.");

        string? title = ToolArguments.Text(arguments, "title");
        string? content = ToolArguments.Text(arguments, "content");
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(content))
            return ToolArguments.Invalid("title and content are required.");

        int importance = ToolArguments.Integer(arguments, "importance") ?? 3;
        if (importance is < 1 or > 5)
            return ToolArguments.Invalid("importance must be between 1 and 5.");

        string? projectName = ToolArguments.Text(arguments, "project");
        ChunkScope scope = projectName is null ? ChunkScope.General : ChunkScope.Project;
        if (ToolArguments.Text(arguments, "scope") is { } scopeText && !ToolArguments.TryEnum(scopeText, out scope))
            return ToolArguments.Invalid("scope must be general or project.");
        if (scope == ChunkScope.Project && projectName is null)
            return ToolArguments.Invalid("project scope needs a project.");

        ProjectId? project = null;
        if (scope == ChunkScope.Project)
        {
            project = await projects.FindAsync(projectName!, cancellationToken);
            if (project is null)
                return new Error("memory.project_not_found", $"There is no project '{projectName}'.", ErrorType.NotFound);
        }

        var draft = new ChunkDraft(scope, project, type, title.Trim(), content.Trim(), importance, ToolArguments.Text(arguments, "sourceRef"));
        Result<ChunkWriteResult> written = await store.WriteAsync(draft, ChunkAuthor.Agent, cancellationToken);
        if (written.IsFailure)
            return written.Error;

        return new ToolOutput(written.Value.Outcome switch
        {
            ChunkWriteOutcome.Created => "Remembered.",
            ChunkWriteOutcome.Updated => "Memory updated.",
            _ => "Already remembered.",
        });
    }
}
