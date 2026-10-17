namespace Marshal.Abstractions.Context;

/// <summary>
/// The embedding of a turn's text, computed once per context build by memory retrieval and reused by tool retrieval.
/// </summary>
public interface ITurnEmbedding
{
    /// <summary>The embedding for <paramref name="request"/>; null without user text or when embedding failed.</summary>
    Task<ReadOnlyMemory<float>?> GetAsync(ContextRequest request, CancellationToken cancellationToken);
}
