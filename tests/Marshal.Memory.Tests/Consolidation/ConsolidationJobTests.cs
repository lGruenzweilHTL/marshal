using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Memory;
using Marshal.Abstractions.Runs;
using Marshal.Abstractions.Sessions;
using Marshal.Data;
using Marshal.Data.Conversations;
using Marshal.Memory.Consolidation;
using Marshal.Memory.Idle;
using Marshal.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using static Marshal.Testing.TestExecutionContext;

namespace Marshal.Memory.Tests.Consolidation;

public sealed class ConsolidationJobTests : IAsyncLifetime
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 30, 2, 0, 0, TimeSpan.Zero));
    private RecordingSummarizer _summarizer = null!;
    private TestDatabase _database = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _database = await TestDatabase.CreateAsync();
        _database.Accessor.Current = Text(OwnerA);
        _summarizer = new RecordingSummarizer(_database.Accessor);
    }

    public async ValueTask DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task Run_SummarizesTheDaysClosedSessionsOfEveryOwnerNotYetSummarized_AsTheirOwner()
    {
        SessionId today = await SessionAsync(OwnerA, closedHoursAgo: 5);
        SessionId otherOwner = await SessionAsync(OwnerB, closedHoursAgo: 20);
        SessionId summarized = await SessionAsync(OwnerA, closedHoursAgo: 3);
        await SummaryRunAsync(OwnerA, summarized, RunOutcome.Ok);
        SessionId failedBefore = await SessionAsync(OwnerA, closedHoursAgo: 4);
        await SummaryRunAsync(OwnerA, failedBefore, RunOutcome.Error);
        await SessionAsync(OwnerA, closedHoursAgo: 30);
        await SessionAsync(OwnerA, closedHoursAgo: null);
        _database.Accessor.Current = Text(OwnerB);

        Result<string> result = await Job().RunAsync(Ct);

        Assert.Equal([(OwnerA, today), (OwnerA, failedBefore), (OwnerB, otherOwner)], _summarizer.Calls.OrderBy(call => call.Owner.Value).ThenBy(call => call.Session.Value));
        Assert.Equal("Consolidated 3 of 3 sessions closed in the last day.", result.Value);
        Assert.Equal(OwnerB, _database.Accessor.Current!.OwnerId);
    }

    [Fact]
    public async Task Run_SummarizerDisabled_Skipped()
    {
        await SessionAsync(OwnerA, closedHoursAgo: 5);

        Result<string> result = await Job(enabled: false).RunAsync(Ct);

        Assert.Empty(_summarizer.Calls);
        Assert.Contains("disabled", result.Value, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Run_AFailedSummary_CountedAndTheRestContinues()
    {
        SessionId first = await SessionAsync(OwnerA, closedHoursAgo: 5);
        await SessionAsync(OwnerA, closedHoursAgo: 6);
        _summarizer.Fails.Add(first);

        Result<string> result = await Job().RunAsync(Ct);

        Assert.Equal(2, _summarizer.Calls.Count);
        Assert.Equal("Consolidated 1 of 2 sessions closed in the last day.", result.Value);
    }

    [Fact]
    public async Task Run_IdleJobsAfterSummaries_PerOwnerInOrder_AFailureDoesNotStopTheOthers()
    {
        await SessionAsync(OwnerA, closedHoursAgo: 5);
        var calls = new List<string>();
        IIdleMemoryJob[] jobs =
        [
            new RecordingJob("contradictions", calls, _database.Accessor, _summarizer, throws: true),
            new RecordingJob("duplicates", calls, _database.Accessor, _summarizer),
        ];

        Result<string> result = await Job(jobs: jobs).RunAsync(Ct);

        Assert.Equal([$"contradictions {OwnerA} after 1", $"duplicates {OwnerA} after 1", $"contradictions {OwnerB} after 1", $"duplicates {OwnerB} after 1"], calls);
        Assert.Contains("Idle-time jobs: 2 failed.", result.Value, StringComparison.Ordinal);
    }

    private ConsolidationJob Job(bool enabled = true, IReadOnlyList<IIdleMemoryJob>? jobs = null) => new(
        _database.Factory,
        _summarizer,
        jobs ?? [],
        new Owners(),
        _database.Accessor,
        Options.Create(new MemoryOptions { Summarizer = new SummarizerOptions { Enabled = enabled } }),
        _time,
        NullLogger<ConsolidationJob>.Instance);

    private async Task<SessionId> SessionAsync(OwnerId owner, int? closedHoursAgo)
    {
        var id = SessionId.New();
        await using MarshalDbContext db = _database.CreateContext(owner);
        DateTimeOffset created = _time.GetUtcNow().AddDays(-2);
        db.Sessions.Add(new Session
        {
            Id = id,
            OwnerId = owner,
            Source = SessionSource.Web,
            IdentityEvidence = "{}",
            ResolvedBy = "cookie",
            CreatedAt = created,
            LastTurnAt = created,
            ClosedAt = closedHoursAgo is { } hours ? _time.GetUtcNow().AddHours(-hours) : null,
        });
        await db.SaveChangesAsync(Ct);
        return id;
    }

    private async Task SummaryRunAsync(OwnerId owner, SessionId session, RunOutcome outcome)
    {
        await using MarshalDbContext db = _database.CreateContext(owner);
        db.Runs.Add(new Run
        {
            Id = RunId.New(),
            OwnerId = owner,
            SessionId = session,
            Source = SessionSource.Task,
            Trigger = LlmMemorySummarizer.Trigger,
            StartedAt = _time.GetUtcNow().AddHours(-1),
            Outcome = outcome,
        });
        await db.SaveChangesAsync(Ct);
    }

    private sealed class Owners : IUserDirectory
    {
        public Task<IReadOnlyList<OwnerId>> ListAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<OwnerId>>([OwnerA, OwnerB]);
    }

    // Records the job, its owner and how many summaries ran before it.
    private sealed class RecordingJob(string name, List<string> calls, IExecutionContextAccessor context, RecordingSummarizer summarizer, bool throws = false) : IIdleMemoryJob
    {
        public string Name => name;

        public Task<Result<int>> RunAsync(DateTimeOffset since, CancellationToken cancellationToken)
        {
            calls.Add($"{name} {context.Current!.OwnerId} after {summarizer.Calls.Count}");
            return throws ? throw new InvalidOperationException("boom") : Task.FromResult(Result.Success(0));
        }
    }

    // Records the owner of the execution context each summary runs in.
    private sealed class RecordingSummarizer(IExecutionContextAccessor context) : IMemorySummarizer
    {
        public List<(OwnerId Owner, SessionId Session)> Calls { get; } = [];

        public HashSet<SessionId> Fails { get; } = [];

        public Task<Result<MemorySummary>> SummarizeAsync(SessionId session, CancellationToken cancellationToken)
        {
            Calls.Add((context.Current!.OwnerId, session));
            return Task.FromResult(Fails.Contains(session)
                ? Result.Failure<MemorySummary>(new Error("memory.summarizer_invalid_output", "Bad output.", ErrorType.Failure))
                : Result.Success(new MemorySummary([], [], 0)));
        }
    }
}
