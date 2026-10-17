using Marshal.Abstractions.Execution;

namespace Marshal.Abstractions.Tools;

/// <summary>
/// Deterministic signs in the turn text that a namespace is needed (an HA entity or area name, a speaker name), for
/// turns whose embedding misses the namespace description.
/// </summary>
public interface IToolNamespaceHints
{
    /// <summary>Namespaces the turn text points to; empty when none.</summary>
    Task<IReadOnlyList<string>> GetHintsAsync(string turnText, MarshalExecutionContext context, CancellationToken cancellationToken);
}
