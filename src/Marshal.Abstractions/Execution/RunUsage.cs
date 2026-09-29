namespace Marshal.Abstractions.Execution;

/// <summary>
/// Model usage of a run (main loop, subagents and advisor calls). Shared by all copies of the run's context.
/// </summary>
public sealed class RunUsage
{
    private readonly Lock _lock = new();
    private decimal _cost;
    private long _tokens;
    private int _modelCalls;

    public decimal Cost
    {
        get
        {
            lock (_lock)
                return _cost;
        }
    }

    public long Tokens
    {
        get
        {
            lock (_lock)
                return _tokens;
        }
    }

    public int ModelCalls
    {
        get
        {
            lock (_lock)
                return _modelCalls;
        }
    }

    /// <summary>Counts one model call with its cost and input + output tokens.</summary>
    public void AddModelCall(decimal cost, long tokens)
    {
        lock (_lock)
        {
            _cost += cost;
            _tokens += tokens;
            _modelCalls++;
        }
    }
}
