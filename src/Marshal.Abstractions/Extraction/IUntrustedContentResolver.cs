namespace Marshal.Abstractions.Extraction;

/// <summary>
/// Resolves typed references of one kind (<c>mail</c>, <c>rss</c>, <c>note</c>, ...) to untrusted content through the
/// current owner's source. Unknown, stale or foreign ids fail with <see cref="ExtractionAnswer.NotFoundCode"/>.
/// </summary>
public interface IUntrustedContentResolver
{
    /// <summary>Reference prefix, e.g. <c>mail</c> for <c>mail:private:4711</c>.</summary>
    string Kind { get; }

    /// <summary>False when the owner has no such source (not configured, no account); <c>agent.extract</c> is hidden while no resolver is available.</summary>
    ValueTask<bool> IsAvailableAsync(CancellationToken cancellationToken);

    /// <summary>Resolves the id part of the reference (after <c>kind:</c>).</summary>
    Task<Result<UntrustedItem>> ResolveAsync(string id, CancellationToken cancellationToken);
}
