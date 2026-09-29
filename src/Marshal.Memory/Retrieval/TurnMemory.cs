using System.Runtime.CompilerServices;
using Marshal.Abstractions.Context;
using Marshal.Abstractions.Memory;
using Marshal.Abstractions.Satellites;
using Microsoft.Extensions.AI;

namespace Marshal.Memory.Retrieval;

/// <summary>
/// Per-turn memory inputs, computed once per context build and shared by the memory contributors: the turn text
/// (with the area name), its embedding and the active projects.
/// </summary>
public interface ITurnMemory
{
    /// <summary>The turn's memory inputs; the same instance for the same <paramref name="request"/>.</summary>
    Task<TurnMemoryContext> GetAsync(ContextRequest request, CancellationToken cancellationToken);
}

/// <summary>Memory inputs of one turn.</summary>
/// <param name="Text">Turn text as embedded (with the area name), or null without user text.</param>
/// <param name="Embedding">Embedding of <paramref name="Text"/>.</param>
/// <param name="ActiveProjects">Projects active for the turn.</param>
public sealed record TurnMemoryContext(string? Text, ReadOnlyMemory<float>? Embedding, IReadOnlySet<ProjectId> ActiveProjects)
{
    /// <summary>A turn without user text: nothing to retrieve.</summary>
    public static TurnMemoryContext Empty { get; } = new(null, null, new HashSet<ProjectId>());
}

/// <summary>
/// <see cref="ITurnMemory"/> that embeds the turn text once per <see cref="ContextRequest"/> instance.
/// </summary>
public sealed class TurnMemory(IEmbeddingGenerator<string, Embedding<float>> embeddings, IAreaDirectory areas) : ITurnMemory
{
    private readonly ConditionalWeakTable<ContextRequest, Task<TurnMemoryContext>> _turns = [];

    public Task<TurnMemoryContext> GetAsync(ContextRequest request, CancellationToken cancellationToken) =>
        _turns.GetValue(request, turn => ComputeAsync(turn, cancellationToken));

    private async Task<TurnMemoryContext> ComputeAsync(ContextRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.UserText))
            return TurnMemoryContext.Empty;

        string? area = request.Context.AreaId is { } areaId ? (await areas.DescribeAsync(areaId, cancellationToken)).AreaName : null;
        string text = area is null ? request.UserText : $"{request.UserText} (in {area})";
        ReadOnlyMemory<float> embedding = (await embeddings.GenerateAsync([text], cancellationToken: cancellationToken)).Single().Vector;
        return new TurnMemoryContext(text, embedding, new HashSet<ProjectId>());
    }
}
