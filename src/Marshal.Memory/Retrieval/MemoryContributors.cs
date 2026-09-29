using System.Globalization;
using System.Text;
using Marshal.Abstractions.Context;
using Marshal.Abstractions.Memory;
using Microsoft.Extensions.Logging;
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
        foreach (MemoryChunk chunk in pinned)
            request.Notes.Add(new ContextNote("memory", chunk.Title, $"{MemoryText.Type(chunk.Type)}, pinned"));

        return pinned.Count == 0 ? null : MemoryText.Section("Pinned memory (always applies):", pinned);
    }
}

/// <summary>
/// Memory retrieved for the turn server-side, without a model tool call, using the turn embedding. When the embedding
/// server or the index fails, the turn continues without retrieved memory (device control must not depend on it).
/// </summary>
public sealed partial class RetrievedMemoryContributor(
    IMemoryRetriever retriever,
    ITurnMemory turns,
    IOptions<MemoryOptions> options,
    ILogger<RetrievedMemoryContributor> logger) : IContextContributor
{
    public int Order => 160;

    public async Task<string?> ContributeAsync(ContextRequest request, CancellationToken cancellationToken)
    {
        TurnMemoryContext turn;
        try
        {
            turn = await turns.GetAsync(request, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Unavailable(request, "embedding failed", exception);
        }

        if (turn.Text is null)
            return null;

        foreach (ProjectActivation activation in turn.Activations)
            request.Notes.Add(new ContextNote("project", activation.Name, MemoryText.Snake(activation.Reason.ToString()), Round(activation.Score)));

        IReadOnlyList<RetrievedChunk> chunks;
        try
        {
            chunks = await retriever.RetrieveAsync(
                new MemoryQuery(turn.Text, turn.Embedding, turn.ActiveProjects, options.Value.Retrieval.TokenBudget), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Unavailable(request, "retrieval failed", exception);
        }

        foreach (RetrievedChunk chunk in chunks)
            request.Notes.Add(new ContextNote("memory", chunk.Chunk.Title, $"{MemoryText.Type(chunk.Chunk.Type)} via {MemoryText.Snake(chunk.Source.ToString())}", Round(chunk.Score)));

        return chunks.Count == 0
            ? null
            : MemoryText.Section("Relevant memory (retrieved automatically; it may be outdated, and what the user says now wins):", [.. chunks.Select(chunk => chunk.Chunk)]);
    }

    private static double? Round(double? score) => score is { } value ? Math.Round(value, 3) : null;

    private string? Unavailable(ContextRequest request, string reason, Exception exception)
    {
        LogUnavailable(logger, exception, reason);
        request.Notes.Add(new ContextNote("memory", "unavailable", reason));
        return null;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Memory retrieval skipped for this turn: {Reason}")]
    private static partial void LogUnavailable(ILogger logger, Exception exception, string reason);
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

    public static string Type(ChunkType type) => Snake(type.ToString());

    public static string Snake(string name) => System.Text.Json.JsonNamingPolicy.SnakeCaseLower.ConvertName(name);
}
