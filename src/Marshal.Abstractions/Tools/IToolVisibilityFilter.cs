using Marshal.Abstractions.Execution;

namespace Marshal.Abstractions.Tools;

/// <summary>
/// Removes every tool the model may not use in a context. The guard applies the same check on every call.
/// </summary>
public interface IToolVisibilityFilter
{
    /// <summary>The tools to offer the model for the next call.</summary>
    Task<IReadOnlyList<RegisteredTool>> GetVisibleToolsAsync(MarshalExecutionContext context, CancellationToken cancellationToken);

    /// <summary>Checks one tool.</summary>
    Task<VisibilityVerdict> CheckAsync(RegisteredTool tool, MarshalExecutionContext context, CancellationToken cancellationToken);
}
