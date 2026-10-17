using Marshal.Abstractions.Context;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Tools;

namespace Marshal.Core.Tools;

/// <summary>
/// Narrows the visible tools to the namespaces a run has loaded (tool retrieval). Never adds a tool the visibility
/// filter removed; security stays with the filter and the guard.
/// </summary>
public interface IToolSelector
{
    /// <summary>
    /// Before a run's first model call: decides whether retrieval applies and loads the core, session, hinted and
    /// retrieved namespaces into <see cref="MarshalExecutionContext.LoadedTools"/>. Adds a context note per namespace.
    /// </summary>
    Task PrepareAsync(IReadOnlyList<RegisteredTool> visible, ToolSelectionRequest request, CancellationToken cancellationToken);

    /// <summary>The tools to offer: visible tools of loaded namespaces, in a stable, append-only order.</summary>
    IReadOnlyList<RegisteredTool> Select(IReadOnlyList<RegisteredTool> visible, MarshalExecutionContext context);

    /// <summary>
    /// Loads the unloaded namespaces with visible tools that best match <paramref name="query"/> (for
    /// <c>agent.find_tools</c>) and returns them.
    /// </summary>
    Task<IReadOnlyList<ToolNamespace>> FindAsync(string query, IReadOnlyList<RegisteredTool> visible, MarshalExecutionContext context, CancellationToken cancellationToken);
}

/// <summary>Input of <see cref="IToolSelector.PrepareAsync"/>.</summary>
/// <param name="Turn">The run's context request (turn text, notes, and the key of the cached turn embedding).</param>
/// <param name="SessionNamespaces">Namespaces earlier turns of the session loaded, in load order.</param>
public sealed record ToolSelectionRequest(ContextRequest Turn, IReadOnlyList<string> SessionNamespaces);
