using System.Globalization;
using System.Text;
using Marshal.Abstractions.Context;
using Marshal.Abstractions.Memory;
using Microsoft.Extensions.Options;

namespace Marshal.Memory.Retrieval;

/// <summary>
/// Pinned chunks, always and in full, before any retrieved memory.
/// </summary>
public sealed class PinnedMemoryContributor(IMemoryRetriever retriever) : IContextContributor
{
    public int Order => 150;

    public async Task<string?> ContributeAsync(ContextRequest request, CancellationToken cancellationToken)
    {
        IReadOnlyList<MemoryChunk> pinned = await retriever.GetPinnedAsync(cancellationToken);
        return pinned.Count == 0 ? null : MemoryText.Section("Pinned memory (always applies):", pinned);
    }
}

/// <summary>
/// Memory retrieved for the turn server-side, without a model tool call, using the turn embedding.
/// </summary>
public sealed class RetrievedMemoryContributor(IMemoryRetriever retriever, ITurnMemory turns, IOptions<MemoryOptions> options) : IContextContributor
{
    public int Order => 160;

    public async Task<string?> ContributeAsync(ContextRequest request, CancellationToken cancellationToken)
    {
        TurnMemoryContext turn = await turns.GetAsync(request, cancellationToken);
        if (turn.Text is null)
            return null;

        IReadOnlyList<RetrievedChunk> chunks = await retriever.RetrieveAsync(
            new MemoryQuery(turn.Text, turn.Embedding, turn.ActiveProjects, options.Value.Retrieval.TokenBudget), cancellationToken);
        return chunks.Count == 0
            ? null
            : MemoryText.Section("Relevant memory (retrieved automatically; it may be outdated, and what the user says now wins):", [.. chunks.Select(chunk => chunk.Chunk)]);
    }
}

/// <summary>How chunks appear in the system prompt and in tool results.</summary>
internal static class MemoryText
{
    public static string Section(string heading, IEnumerable<MemoryChunk> chunks)
    {
        StringBuilder text = new StringBuilder(heading).Append('\n');
        foreach (MemoryChunk chunk in chunks)
            text.AppendLine(Line(chunk));

        return text.ToString();
    }

    public static string Line(MemoryChunk chunk) =>
        string.Create(CultureInfo.InvariantCulture, $"- [{Type(chunk.Type)}] {chunk.Title}: {chunk.Content}");

    private static string Type(ChunkType type) => System.Text.Json.JsonNamingPolicy.SnakeCaseLower.ConvertName(type.ToString());
}
