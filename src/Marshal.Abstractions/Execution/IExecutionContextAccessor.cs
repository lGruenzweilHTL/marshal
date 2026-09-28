namespace Marshal.Abstractions.Execution;

/// <summary>
/// Gives access to the execution context of the current run or request.
/// </summary>
public interface IExecutionContextAccessor
{
    /// <summary>The current context, or null outside any run or request.</summary>
    MarshalExecutionContext? Current { get; }

    /// <summary>Makes <paramref name="context"/> current until the returned scope is disposed.</summary>
    IDisposable Use(MarshalExecutionContext context);
}
