using Marshal.Abstractions;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Memory;
using Marshal.Abstractions.Sessions;
using Marshal.Memory.Consolidation;
using Marshal.Memory.Projects;
using Marshal.Testing;
using Microsoft.Extensions.Logging.Abstractions;

namespace Marshal.Memory.Tests.Consolidation;

public sealed class SessionSummaryInboxTests
{
    private readonly FakeExecutionContextAccessor _accessor = new();
    private readonly ProjectSessions _projectSessions = new();
    private readonly RecordingSummarizer _summarizer;

    public SessionSummaryInboxTests() => _summarizer = new RecordingSummarizer(_accessor);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Closed_ForgetsProjectStateAndSummarizesAsTheOwner()
    {
        var session = SessionId.New();
        ProjectSessionState before = _projectSessions.Get(session);
        SessionSummaryInbox inbox = Inbox(new MemoryOptions());
        using SessionSummaryWorker worker = Worker(inbox);
        await worker.StartAsync(Ct);

        await inbox.SessionClosedAsync(TestExecutionContext.OwnerB, session, Ct);

        Assert.Equal((TestExecutionContext.OwnerB, session), await _summarizer.Next.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct));
        Assert.NotSame(before, _projectSessions.Get(session));
        await worker.StopAsync(Ct);
    }

    [Fact]
    public async Task Closed_SummarizerDisabled_NotSummarized()
    {
        SessionSummaryInbox inbox = Inbox(new MemoryOptions { Summarizer = { Enabled = false } });

        await inbox.SessionClosedAsync(TestExecutionContext.OwnerA, SessionId.New(), Ct);

        Assert.False(inbox.Reader.TryRead(out _));
    }

    [Fact]
    public async Task Worker_SummarizerThrows_ContinuesWithNextSession()
    {
        SessionSummaryInbox inbox = Inbox(new MemoryOptions());
        using SessionSummaryWorker worker = Worker(inbox);
        await worker.StartAsync(Ct);
        var failing = SessionId.New();
        var next = SessionId.New();
        _summarizer.Failing.Add(failing);

        await inbox.SessionClosedAsync(TestExecutionContext.OwnerA, failing, Ct);
        await inbox.SessionClosedAsync(TestExecutionContext.OwnerA, next, Ct);

        Assert.Equal((TestExecutionContext.OwnerA, next), await _summarizer.Next.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct));
        await worker.StopAsync(Ct);
    }

    private SessionSummaryInbox Inbox(MemoryOptions options) =>
        new(_projectSessions, Microsoft.Extensions.Options.Options.Create(options));

    private SessionSummaryWorker Worker(SessionSummaryInbox inbox) =>
        new(inbox, _summarizer, _accessor, NullLogger<SessionSummaryWorker>.Instance);

    private sealed class RecordingSummarizer(FakeExecutionContextAccessor accessor) : IMemorySummarizer
    {
        public TaskCompletionSource<(OwnerId, SessionId)> Next { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public HashSet<SessionId> Failing { get; } = [];

        public Task<Result<MemorySummary>> SummarizeAsync(SessionId session, CancellationToken cancellationToken)
        {
            if (Failing.Contains(session))
                throw new InvalidOperationException("Model exploded.");

            Next.TrySetResult((accessor.Current!.OwnerId, session));
            return Task.FromResult<Result<MemorySummary>>(new MemorySummary([], [], 0));
        }
    }
}
