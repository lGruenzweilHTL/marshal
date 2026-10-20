using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Memory;
using Marshal.Abstractions.Runs;
using Marshal.Abstractions.Execution;
using Marshal.Data;
using Marshal.Data.Conversations;
using Marshal.Data.Memory;
using Marshal.Memory.Idle;
using Marshal.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using static Marshal.Memory.Tests.Retrieval.HybridMemoryRetrieverTests;

namespace Marshal.Memory.Tests.Idle;

public sealed class IdleMemoryJobsTests : IAsyncLifetime
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 20, 1, 0, 0, TimeSpan.Zero));
    private readonly FakeEmbeddingGenerator _embeddings = new();
    private readonly FakeChatClient _model = new();
    private readonly InMemoryRunRecorder _runs = new();
    private TestDatabase _database = null!;
    private SqliteMemoryStore _store = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private DateTimeOffset Since => _time.GetUtcNow().AddDays(-1);

    public async ValueTask InitializeAsync()
    {
        _database = await TestDatabase.CreateAsync();
        _database.Accessor.Current = TestExecutionContext.Text();
        _store = MemoryTestKit.Store(_database, _embeddings, _time);
    }

    public async ValueTask DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task Contradiction_OpenPair_ProposedOnceWithCurrentChunkAndCloseTime()
    {
        (ChunkId old, ChunkId current) = await ContradictionAsync();
        _model.ThenText(JsonSerializer.Serialize(new { current = Letter(current, old, current), since = "2026-03-01", reason = "The newer statement replaces the old nozzle." }));

        Result<int> first = await Contradictions().RunAsync(Since, Ct);
        Result<int> second = await Contradictions().RunAsync(Since, Ct);

        Assert.Equal((1, 0), (first.Value, second.Value));
        Assert.Single(_model.Requests);
        MemoryProposal proposal = Assert.Single(await ProposalsAsync());
        Assert.Equal((MemoryProposalKind.Contradiction, MemoryProposalStatus.Open), (proposal.Kind, proposal.Status));
        JsonElement suggestion = JsonDocument.Parse(proposal.SuggestionJson).RootElement;
        Assert.Equal((current.Value, old.Value), (suggestion.GetProperty("current").GetString(), suggestion.GetProperty("outdated").GetString()));
        Assert.Equal(new DateTimeOffset(2026, 2, 28, 23, 0, 0, TimeSpan.Zero), suggestion.GetProperty("closeAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task Contradiction_DismissedPair_NotProposedAgain()
    {
        (ChunkId old, ChunkId current) = await ContradictionAsync();
        await AddProposalAsync(MemoryProposalKind.Contradiction, old, current, MemoryProposalStatus.Dismissed);

        Assert.Equal(0, (await Contradictions().RunAsync(Since, Ct)).Value);
        Assert.Empty(_model.Requests);
    }

    [Fact]
    public async Task Contradiction_ClosedChunk_NoLongerOpen()
    {
        (ChunkId old, _) = await ContradictionAsync();
        await _store.CloseAsync(old, _time.GetUtcNow().AddHours(-1), Ct);

        Assert.Equal(0, (await Contradictions().RunAsync(Since, Ct)).Value);
        Assert.Empty(_model.Requests);
    }

    [Fact]
    public async Task Contradiction_InvalidModelOutput_NoProposalRunFailed()
    {
        await ContradictionAsync();
        _model.ThenText("not json");

        await Contradictions().RunAsync(Since, Ct);

        Assert.Empty(await ProposalsAsync());
        Assert.Equal(RunOutcome.Error, _runs.OnlyRun.Completion!.Outcome);
    }

    [Fact]
    public async Task Duplicate_SameOwnerTypeScopeAboveThreshold_OneProposalWithMergedText()
    {
        ChunkId oat = await WriteAsync(ChunkType.Preference, "Coffee milk", "Likes oat milk.", Vec(0, 1));
        ChunkId coffee = await WriteAsync(ChunkType.Preference, "Coffee", "Prefers oat milk in coffee.", Vec(0, 1, 1, 0.33f));
        await WriteAsync(ChunkType.Fact, "Oat milk stock", "Two cartons in the pantry.", Vec(0, 1));
        await WriteAsync(ChunkType.Preference, "Tea", "Drinks green tea.", Vec(0, 1, 1, 1));
        _model.ThenText(JsonSerializer.Serialize(new { keep = Letter(coffee, oat, coffee), title = "Coffee", content = "Prefers oat milk in coffee." }));

        Result<int> result = await Duplicates().RunAsync(Since, Ct);

        Assert.Equal(1, result.Value);
        MemoryProposal proposal = Assert.Single(await ProposalsAsync());
        Assert.Equal((MemoryProposalKind.Duplicate, MemoryProposal.Pair(oat, coffee)), (proposal.Kind, (proposal.ChunkA, proposal.ChunkB)));
        JsonElement suggestion = JsonDocument.Parse(proposal.SuggestionJson).RootElement;
        Assert.Equal((coffee.Value, oat.Value, "Coffee", "Prefers oat milk in coffee."),
            (suggestion.GetProperty("keep").GetString(), suggestion.GetProperty("other").GetString(), suggestion.GetProperty("title").GetString(), suggestion.GetProperty("content").GetString()));
        Assert.True(suggestion.GetProperty("similarity").GetDouble() >= 0.92);
    }

    [Fact]
    public async Task Duplicate_NothingChangedSinceTheLastRun_NoModelCall()
    {
        await WriteAsync(ChunkType.Preference, "Coffee milk", "Likes oat milk.", Vec(0, 1));
        await WriteAsync(ChunkType.Preference, "Coffee", "Prefers oat milk in coffee.", Vec(0, 1, 1, 0.33f));
        _time.Advance(TimeSpan.FromDays(2));

        Assert.Equal(0, (await Duplicates().RunAsync(Since, Ct)).Value);
        Assert.Empty(_model.Requests);
    }

    [Fact]
    public async Task Duplicate_DismissedPair_NotProposedAgain()
    {
        ChunkId oat = await WriteAsync(ChunkType.Preference, "Coffee milk", "Likes oat milk.", Vec(0, 1));
        ChunkId coffee = await WriteAsync(ChunkType.Preference, "Coffee", "Prefers oat milk in coffee.", Vec(0, 1, 1, 0.33f));
        await AddProposalAsync(MemoryProposalKind.Duplicate, oat, coffee, MemoryProposalStatus.Dismissed);

        Assert.Equal(0, (await Duplicates().RunAsync(Since, Ct)).Value);
        Assert.Empty(_model.Requests);
    }

    [Fact]
    public async Task ProjectSummary_ChangedProject_SummaryCappedAt120Words()
    {
        ProjectId voron = await MemoryTestKit.AddProjectAsync(_database, "Voron", "Voron 2.4 build");
        await WriteAsync(ChunkType.Device, "Voron nozzle", "Uses a 0.6 mm nozzle.", Vec(0, 1), voron);
        _model.ThenText(JsonSerializer.Serialize(new { summary = string.Join(' ', Enumerable.Repeat("word", 150)) }));

        Assert.Equal(1, (await Summaries().RunAsync(Since, Ct)).Value);

        string input = string.Join("\n", Assert.Single(_model.Requests).Messages.Select(message => message.Text));
        Assert.Contains("Voron 2.4 build", input, StringComparison.Ordinal);
        Assert.Contains("Uses a 0.6 mm nozzle.", input, StringComparison.Ordinal);
        await using MarshalDbContext db = _database.CreateContext();
        Project project = await db.Projects.SingleAsync(Ct);
        Assert.Equal((120, (DateTimeOffset?)_time.GetUtcNow()), (project.Summary!.Split(' ').Length, project.SummaryUpdatedAt));
    }

    [Fact]
    public async Task ProjectSummary_NoChangeSinceTheLastSummary_NoModelCall()
    {
        ProjectId voron = await MemoryTestKit.AddProjectAsync(_database, "Voron", "Voron 2.4 build");
        await WriteAsync(ChunkType.Device, "Voron nozzle", "Uses a 0.6 mm nozzle.", Vec(0, 1), voron);
        _model.ThenText(JsonSerializer.Serialize(new { summary = "A Voron 2.4 with a 0.6 mm nozzle." }));
        await Summaries().RunAsync(Since, Ct);
        _time.Advance(TimeSpan.FromDays(1));

        Assert.Equal(0, (await Summaries().RunAsync(Since, Ct)).Value);
        Assert.Single(_model.Requests);
    }

    [Fact]
    public async Task Jobs_RecordedAsTaskRunsWithCost_SoTheDailyCapCountsThem()
    {
        await ContradictionAsync();
        _model.ThenText(JsonSerializer.Serialize(new { current = "a", since = (string?)null, reason = "r" }));

        await Contradictions().RunAsync(Since, Ct);

        RecordedRun run = _runs.OnlyRun;
        Assert.Equal((SessionSource.Task, "memory_idle:contradictions"), (run.Start.Context.Source, run.Start.Trigger));
        Assert.Equal(0.01m, Assert.Single(run.Steps, step => step.Kind == RunStepKind.ModelCall).Cost);
    }

    [Fact]
    public async Task Jobs_DailyCapUsedUp_NoModelCall()
    {
        await ContradictionAsync();

        Result<int> result = await new ContradictionProposalJob(_database.Factory, IdleTestKit.Calls(_database, _model, _runs, _time, capLeft: 0), new ViennaProfiles(), _time).RunAsync(Since, Ct);

        Assert.Equal("memory.cost_cap_reached", result.Error.Code);
        Assert.Empty(_model.Requests);
    }

    [Fact]
    public async Task Jobs_ReadOnlyMemory_NeverSessionsOrToolResults()
    {
        (_, _) = await ContradictionAsync();
        await SessionTurnAsync("SESSION TEXT about the nozzle", "TOOL RESULT nozzle mail");
        _model.ThenText(JsonSerializer.Serialize(new { current = "a", since = (string?)null, reason = "r" }));

        await Contradictions().RunAsync(Since, Ct);

        string input = string.Join("\n", Assert.Single(_model.Requests).Messages.Select(message => message.Text));
        Assert.DoesNotContain("SESSION TEXT", input, StringComparison.Ordinal);
        Assert.DoesNotContain("TOOL RESULT", input, StringComparison.Ordinal);
    }

    private ContradictionProposalJob Contradictions() => new(_database.Factory, IdleTestKit.Calls(_database, _model, _runs, _time), new ViennaProfiles(), _time);

    private DuplicateProposalJob Duplicates() => new(_database.Factory, IdleTestKit.Calls(_database, _model, _runs, _time), Microsoft.Extensions.Options.Options.Create(new MemoryOptions()), _time);

    private ProjectSummaryJob Summaries() => new(_database.Factory, IdleTestKit.Calls(_database, _model, _runs, _time), _time);

    // The model names chunks "a" and "b" in stored pair order.
    private static string Letter(ChunkId chunk, ChunkId first, ChunkId second) => MemoryProposal.Pair(first, second).A == chunk ? "a" : "b";

    private async Task<(ChunkId Old, ChunkId Current)> ContradictionAsync()
    {
        ChunkId old = await WriteAsync(ChunkType.Device, "Voron nozzle", "0.4 mm nozzle.", Vec(0, 1));
        ChunkId current = await WriteAsync(ChunkType.Device, "Voron nozzle (conflicting)", "0.6 mm nozzle.", Vec(0, 1, 1, 0.1f));
        await _store.LinkAsync(current, old, ChunkEdgeKind.Contradicts, Ct);
        return (old, current);
    }

    private async Task<ChunkId> WriteAsync(ChunkType type, string title, string content, float[] vector, ProjectId? project = null)
    {
        _embeddings.Pin($"{title}\n{content}", vector);
        var draft = new ChunkDraft(project is null ? ChunkScope.General : ChunkScope.Project, project, type, title, content);
        return (await _store.WriteAsync(draft, ChunkAuthor.User, Ct)).Value.Id;
    }

    private async Task AddProposalAsync(MemoryProposalKind kind, ChunkId first, ChunkId second, MemoryProposalStatus status)
    {
        (ChunkId a, ChunkId b) = MemoryProposal.Pair(first, second);
        await using MarshalDbContext db = _database.CreateContext();
        db.MemoryProposals.Add(new MemoryProposal
        {
            Id = Guid.CreateVersion7(),
            OwnerId = TestExecutionContext.OwnerA,
            Kind = kind,
            ChunkA = a,
            ChunkB = b,
            SuggestionJson = "{}",
            Status = status,
            CreatedAt = _time.GetUtcNow(),
        });
        await db.SaveChangesAsync(Ct);
    }

    private async Task<List<MemoryProposal>> ProposalsAsync()
    {
        await using MarshalDbContext db = _database.CreateContext();
        return await db.MemoryProposals.ToListAsync(Ct);
    }

    private async Task SessionTurnAsync(string input, string toolResult)
    {
        var recorder = new DbRunRecorder(_database.Factory, [], Microsoft.Extensions.Options.Options.Create(new RecordingOptions()), _time);
        RunId run = await recorder.StartRunAsync(new RunStart(TestExecutionContext.Text(), "turn"), Ct);
        await recorder.RecordStepAsync(run, new RunStepRecord { Kind = RunStepKind.Context, Content = "system", Input = input }, Ct);
        await recorder.RecordStepAsync(run, new RunStepRecord { Kind = RunStepKind.ToolCall, Tool = new Abstractions.Tools.ToolId("mail", "read"), Content = toolResult }, Ct);
        await recorder.CompleteRunAsync(run, new RunCompletion(RunOutcome.Ok, null, []), Ct);
    }
}
