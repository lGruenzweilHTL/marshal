using Marshal.Abstractions.Sessions;

namespace Marshal.Abstractions.Memory;

/// <summary>
/// Turns a closed session into memory: a cheap model proposes chunk candidates from the user turns and replies (never
/// tool results); they are written through their deterministic ids, and contradictions become <c>contradicts</c>
/// edges instead of overwrites.
/// </summary>
public interface IMemorySummarizer
{
    Task<Result<MemorySummary>> SummarizeAsync(SessionId session, CancellationToken cancellationToken);
}

/// <summary>What a summarization wrote.</summary>
/// <param name="Written">Chunks created or updated from candidates.</param>
/// <param name="Contradictions">Chunks linked by a <c>contradicts</c> edge to the chunk they contradict.</param>
/// <param name="Skipped">Invalid candidates and candidates for deleted chunks.</param>
public sealed record MemorySummary(IReadOnlyList<ChunkId> Written, IReadOnlyList<ChunkId> Contradictions, int Skipped);
