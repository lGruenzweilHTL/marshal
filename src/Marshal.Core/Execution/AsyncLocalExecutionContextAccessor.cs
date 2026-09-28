using Marshal.Abstractions.Execution;

namespace Marshal.Core.Execution;

/// <summary>
/// Execution context that flows with the async call chain. Nested scopes restore the outer context on dispose.
/// </summary>
public sealed class AsyncLocalExecutionContextAccessor : IExecutionContextAccessor
{
    private static readonly AsyncLocal<MarshalExecutionContext?> _current = new();

    public MarshalExecutionContext? Current => _current.Value;

    public IDisposable Use(MarshalExecutionContext context)
    {
        MarshalExecutionContext? previous = _current.Value;
        _current.Value = context;
        return new Scope(previous);
    }

    private sealed class Scope(MarshalExecutionContext? previous) : IDisposable
    {
        public void Dispose() => _current.Value = previous;
    }
}
