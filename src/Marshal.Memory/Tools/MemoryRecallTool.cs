using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Memory;
using Marshal.Abstractions.Tools;
using Marshal.Memory.Projects;
using Marshal.Memory.Retrieval;
using Microsoft.Extensions.Options;

namespace Marshal.Memory.Tools;

/// <summary>
/// <c>memory.recall(query, type?, project?, limit?)</c>: deliberate deep search with a larger budget than automatic
/// retrieval, including pinned chunks. Memory is written by the user or through confirmed writes, so it is trusted.
/// </summary>
public sealed class MemoryRecallTool(IMemoryRetriever retriever, IMemoryProjects projects, IOptions<MemoryOptions> options) : ITool
{
    private const int MaxLimit = 30;

    public ToolDescriptor Descriptor { get; } = new()
    {
        Id = new ToolId("memory", "recall"),
        Description = "Searches long-term memory more deeply than the automatic memory in the system prompt. Use it when that memory does not answer the question.",
        ParametersSchema = JsonDocument.Parse("""
            {"type":"object","properties":{
              "query":{"type":"string","description":"What to look for, in English."},
              "type":{"type":"string","enum":["fact","preference","event","procedure","person","device"],"description":"Only chunks of this type."},
              "project":{"type":"string","description":"Only chunks of this project (name or alias)."},
              "limit":{"type":"integer","minimum":1,"maximum":30,"description":"Maximum number of chunks (default 10)."}},
             "required":["query"]}
            """).RootElement.Clone(),
        DefaultTier = RiskTier.Safe,
    };

    public string Summarize(JsonElement arguments, string language) => $"recall {ToolArguments.Text(arguments, "query")}";

    public async Task<Result<ToolOutput>> InvokeAsync(ToolInvocation invocation, CancellationToken cancellationToken)
    {
        JsonElement arguments = invocation.Arguments;
        string? query = ToolArguments.Text(arguments, "query");
        if (string.IsNullOrWhiteSpace(query))
            return ToolArguments.Invalid("query is required.");

        ChunkType? type = null;
        if (ToolArguments.Text(arguments, "type") is { } typeText)
        {
            if (!ToolArguments.TryEnum(typeText, out ChunkType parsed))
                return ToolArguments.Invalid($"type '{typeText}' must be one of fact, preference, event, procedure, person, device.");
            type = parsed;
        }

        ProjectId? project = null;
        if (ToolArguments.Text(arguments, "project") is { } projectName)
        {
            project = await projects.FindAsync(projectName, cancellationToken);
            if (project is null)
                return new Error("memory.project_not_found", $"There is no project '{projectName}'.", ErrorType.NotFound);
        }

        int limit = Math.Clamp(ToolArguments.Integer(arguments, "limit") ?? 10, 1, MaxLimit);
        HashSet<ProjectId> active = project is { } filter ? [filter] : [];
        IReadOnlyList<RetrievedChunk> chunks = await retriever.RetrieveAsync(
            new MemoryQuery(query, null, active, options.Value.Retrieval.RecallTokenBudget, type, project, IncludePinned: true),
            cancellationToken);
        return chunks.Count == 0
            ? new ToolOutput("No memory found.")
            : new ToolOutput(MemoryText.Section($"Memory matching '{query}':", chunks.Take(limit).Select(chunk => chunk.Chunk)).TrimEnd());
    }
}
