using Marshal.Abstractions.Execution;

namespace Marshal.Testing;

/// <summary>
/// Execution context accessor with a settable current context (not flowing across async calls).
/// </summary>
public sealed class FakeExecutionContextAccessor(MarshalExecutionContext? current = null) : IExecutionContextAccessor
{
    /// <inheritdoc />
    public MarshalExecutionContext? Current { get; set; } = current;

    /// <inheritdoc />
    public IDisposable Use(MarshalExecutionContext context)
    {
        MarshalExecutionContext? previous = Current;
        Current = context;
        return new Restore(() => Current = previous);
    }

    private sealed class Restore(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }
}
