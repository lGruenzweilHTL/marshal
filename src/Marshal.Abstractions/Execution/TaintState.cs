namespace Marshal.Abstractions.Execution;

/// <summary>
/// Taint of a run: set when untrusted content enters the context, never cleared within the run.
/// </summary>
public sealed class TaintState
{
    private readonly Lock _lock = new();
    private readonly List<TaintSource> _sources = [];

    /// <summary>True once any untrusted content entered the run.</summary>
    public bool IsTainted
    {
        get
        {
            lock (_lock)
            {
                return _sources.Count > 0;
            }
        }
    }

    /// <summary>The distinct source kinds, each with the first step that brought it in.</summary>
    public IReadOnlyList<TaintSource> Sources
    {
        get
        {
            lock (_lock)
            {
                return [.. _sources];
            }
        }
    }

    /// <summary>Marks the run tainted by <paramref name="kind"/>. Repeated kinds keep their first step.</summary>
    public void Mark(string kind, int step)
    {
        lock (_lock)
        {
            if (_sources.Exists(source => source.Kind == kind))
                return;

            _sources.Add(new TaintSource(kind, step));
        }
    }
}
