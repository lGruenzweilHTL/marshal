using System.Collections.Concurrent;

namespace Marshal.Abstractions.Execution;

/// <summary>
/// Per-run counters, e.g. how often a limited tool was called in this run.
/// </summary>
public sealed class RunCounters
{
    private readonly ConcurrentDictionary<string, int> _counts = new(StringComparer.Ordinal);

    /// <summary>Current value for <paramref name="key"/> (0 when never incremented).</summary>
    public int Get(string key) => _counts.GetValueOrDefault(key);

    /// <summary>Increments the counter and returns the new value.</summary>
    public int Increment(string key) => _counts.AddOrUpdate(key, 1, (_, count) => count + 1);
}
