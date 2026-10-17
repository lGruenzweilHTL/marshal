using System.Collections.Concurrent;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Guard;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Runs;
using Marshal.Abstractions.Tools;
using Microsoft.EntityFrameworkCore;

namespace Marshal.Data.Conversations;

/// <summary>
/// Median duration of a tool's last 20 executed calls from the run steps, cached for 5 minutes. Fewer than 5
/// recorded calls give no estimate.
/// </summary>
public sealed class RecordedToolLatencyStats(IDbContextFactory<MarshalDbContext> contexts, IExecutionContextAccessor executionContext, TimeProvider timeProvider) : IToolLatencyStats
{
    private const int Window = 20;
    private const int MinimumCalls = 5;
    private static readonly TimeSpan _cacheFor = TimeSpan.FromMinutes(5);
    private static readonly GuardDecision[] _notExecuted = [GuardDecision.Pending, GuardDecision.EscalatedByTaint, GuardDecision.Denied, GuardDecision.Expired];

    private readonly ConcurrentDictionary<(OwnerId Owner, string Tool), (DateTimeOffset At, TimeSpan? Expected)> _cache = new();

    public async Task<TimeSpan?> ExpectedDurationAsync(ToolId tool, CancellationToken cancellationToken)
    {
        if (executionContext.Current is not { } context)
            return null;

        DateTimeOffset now = timeProvider.GetUtcNow();
        (OwnerId Owner, string Tool) key = (context.OwnerId, tool.ToString());
        if (_cache.TryGetValue(key, out (DateTimeOffset At, TimeSpan? Expected) cached) && now - cached.At < _cacheFor)
            return cached.Expected;

        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        string id = tool.ToString();
        List<int> durations = await db.RunSteps
            .Where(step => step.Kind == RunStepKind.ToolCall && step.Tool == id && (step.GuardDecision == null || !_notExecuted.Contains(step.GuardDecision.Value)))
            .Join(db.Runs, step => step.RunId, run => run.Id, (step, run) => new { step.DurationMs, run.StartedAt, step.Sequence })
            .OrderByDescending(row => row.StartedAt)
            .ThenByDescending(row => row.Sequence)
            .Take(Window)
            .Select(row => row.DurationMs)
            .ToListAsync(cancellationToken);

        TimeSpan? expected = durations.Count < MinimumCalls ? null : TimeSpan.FromMilliseconds(Median(durations));
        _cache[key] = (now, expected);
        return expected;
    }

    private static double Median(List<int> values)
    {
        values.Sort();
        int middle = values.Count / 2;
        return values.Count % 2 == 1 ? values[middle] : (values[middle - 1] + values[middle]) / 2.0;
    }
}
