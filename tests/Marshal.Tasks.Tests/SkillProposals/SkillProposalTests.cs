using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Evals;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Models;
using Marshal.Abstractions.Notifications;
using Marshal.Abstractions.Runs;
using Marshal.Abstractions.Suggestions;
using Marshal.Data;
using Marshal.Data.Conversations;
using Marshal.Tasks.SkillProposals;
using Marshal.Tasks.Suggestions;
using Marshal.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using static Marshal.Testing.TestExecutionContext;

namespace Marshal.Tasks.Tests.SkillProposals;

public sealed class SkillProposalTests : IAsyncLifetime
{
    private static readonly string[] Warmup = ["printer.status", "ha.HassTurnOn", "printer.preheat"];

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 21, 2, 0, 0, TimeSpan.Zero));
    private readonly FakeChatClient _model = new();
    private readonly InMemoryRunRecorder _recorded = new();
    private readonly FakeNotificationSender _sender = new();
    private readonly FakeUserPreferencesStore _preferences = new();
    private readonly FakeSkillCatalog _skills = new();
    private readonly FakeEmbeddingGenerator _embeddings = new();
    private TestDatabase _database = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _database = await TestDatabase.CreateAsync();
        _preferences.Stored[OwnerA] = new UserPreferencesRecord("en", "Europe/Vienna", [], NotificationTargets.None, null, HelpMarshalLearn: true);
        // Similar requests: close vectors; the mail request points elsewhere.
        _embeddings.Pin("get the printer ready", 1, 0, 0).Pin("get the printer ready please", 0.95f, 0.1f, 0).Pin("warm up the voron", 0.9f, 0.15f, 0)
            .Pin("prepare the printer", 0.97f, 0.05f, 0).Pin("summarize my mail", 0, 0, 1);
    }

    public async ValueTask DisposeAsync() => await _database.DisposeAsync();

    // 3.1: candidate runs and grouping

    [Fact]
    public async Task Reader_OnlyOkRunsWithThreeToolCalls_NotRatedDown_Within30Days_OwnerScoped()
    {
        RunId ok = await SeedRunAsync(OwnerA, "get the printer ready", Warmup);
        await SeedRunAsync(OwnerA, "get the printer ready", Warmup, outcome: RunOutcome.Error);
        await SeedRunAsync(OwnerA, "get the printer ready", Warmup, rating: RunRating.Down);
        await SeedRunAsync(OwnerA, "get the printer ready", ["printer.status", "printer.preheat"]);
        await SeedRunAsync(OwnerA, "get the printer ready", Warmup, age: TimeSpan.FromDays(31));
        await SeedRunAsync(OwnerB, "get the printer ready", Warmup);
        RunId up = await SeedRunAsync(OwnerA, "get the printer ready please", Warmup, rating: RunRating.Up);

        IReadOnlyList<WorkflowRun> runs = await AsAsync(OwnerA, () => Reader().ListAsync(_time.GetUtcNow() - TimeSpan.FromDays(30), 3, Ct));

        Assert.Equal([ok, up], runs.Select(run => run.Id));
        WorkflowRun first = runs[0];
        Assert.Equal("get the printer ready", first.Input);
        Assert.Equal(Warmup, first.Tools);
        Assert.Equal(["printer"], first.ArgumentKeys[0]);
        Assert.Equal(["name", "domain"], first.ArgumentKeys[1]);
    }

    [Fact]
    public async Task Reader_EmbeddingCachedOnTheRun()
    {
        RunId run = await SeedRunAsync(OwnerA, "get the printer ready", Warmup);

        await AsAsync(OwnerA, () => Reader().SaveEmbeddingAsync(run, [0.5f, -1.25f, 2f], Ct));

        WorkflowRun read = Assert.Single(await AsAsync(OwnerA, () => Reader().ListAsync(_time.GetUtcNow().AddDays(-30), 3, Ct)));
        Assert.Equal([0.5f, -1.25f, 2f], read.Embedding!);
    }

    [Fact]
    public void Grouper_EqualSequencesAndSimilarInputs_GroupsOfThreeOrMore()
    {
        WorkflowRun[] runs =
        [
            Run("a", Warmup, 1, 0, 0),
            Run("b", Warmup, 0.95f, 0.1f, 0),
            Run("c", Warmup, 0.9f, 0.15f, 0),
            Run("d", Warmup, 0, 0, 1),
            Run("e", ["printer.status", "printer.preheat", "ha.HassTurnOn"], 1, 0, 0),
            Run("f", ["printer.status", "printer.preheat", "ha.HassTurnOn"], 1, 0, 0),
        ];

        WorkflowGroup group = Assert.Single(WorkflowGrouper.Group(runs));

        Assert.Equal(["a", "b", "c"], group.Runs.Select(run => run.Input));
        Assert.Equal(WorkflowGrouper.Hash(string.Join('\n', Warmup)), group.SequenceHash);
    }

    [Fact]
    public void Grouper_DissimilarInputsWithTheSameSequence_Split()
    {
        WorkflowRun[] runs = [Run("a", Warmup, 1, 0, 0), Run("b", Warmup, 0, 1, 0), Run("c", Warmup, 0, 0, 1)];

        Assert.Empty(WorkflowGrouper.Group(runs));
    }

    // 3.2: drafting

    [Fact]
    public async Task UncoveredWorkflow_DraftsSkillMd_OneLowNotification_NoResultsOrValuesInInput()
    {
        await SeedWarmupRunsAsync();
        _model.ThenText(Draft("printer-warmup", "Gets the printer ready to print. Use when the user asks to warm up or prepare the printer."));

        Result<string> result = await Job().RunAsync(Ct);

        Assert.Equal("1 skill proposal(s) drafted.", result.Value);
        SkillProposalRecord proposal = Assert.Single(await AsAsync(OwnerA, () => Store().ListAsync(Ct)));
        Assert.Equal("printer-warmup", proposal.Name);
        Assert.StartsWith("---\nname: printer-warmup\ndescription: Gets the printer ready to print. Use when the user asks to warm up or prepare the printer.\n---\n\n# Printer warm-up", proposal.Content);
        Assert.Null(proposal.Diff);
        Assert.Equal(3, proposal.SourceRuns.Count);
        string input = _model.Requests.Single().Messages.Single(message => message.Role == ChatRole.User).Text;
        Assert.Contains("printer_status", input);
        Assert.Contains("(arguments: name, domain)", input);
        Assert.Contains("get the printer ready", input);
        Assert.DoesNotContain("Bed at 21", input);
        Assert.DoesNotContain("Voron power", input);
        Assert.Equal("skill_proposal", _recorded.OnlyRun.Start.Trigger);
        NotificationRequest note = Assert.Single(_sender.Sent);
        Assert.Equal(NotificationPriority.Low, note.Priority);
        Assert.Contains("printer-warmup", note.Text);
    }

    [Fact]
    public async Task CoveredWorkflow_NothingShown_NotAskedAgain()
    {
        await SeedWarmupRunsAsync();
        _skills.Items.Add(FakeSkillCatalog.Skill("printer-prep", "Prepares the printer."));
        _model.ThenText("""{"covered":true,"name":"","description":"","body":""}""");

        await Job().RunAsync(Ct);
        await Job().RunAsync(Ct);

        Assert.Empty(await AsAsync(OwnerA, () => Store().ListAsync(Ct)));
        Assert.Single(_model.Requests);
        Assert.Contains("printer-prep: Prepares the printer.", _model.Requests[0].Messages.Single(message => message.Role == ChatRole.User).Text);
        Assert.Empty(_sender.Sent);
    }

    [Fact]
    public async Task DismissedWorkflow_NotProposedAgain()
    {
        await SeedWarmupRunsAsync();
        _model.ThenText(Draft("printer-warmup", "Warms up the printer."));
        await Job().RunAsync(Ct);
        SkillProposalRecord proposal = Assert.Single(await AsAsync(OwnerA, () => Store().ListAsync(Ct)));

        await AsAsync(OwnerA, () => Store().DismissAsync(proposal.Id, Ct));
        await SeedRunAsync(OwnerA, "warm up the voron", Warmup);
        await Job().RunAsync(Ct);

        Assert.Single(_model.Requests);
        Assert.Empty(await AsAsync(OwnerA, () => Store().ListAsync(Ct)));
    }

    [Fact]
    public async Task SameNameSkillExists_ProposalCarriesDiff()
    {
        await SeedWarmupRunsAsync();
        _skills.Items.Add(FakeSkillCatalog.Skill("printer-warmup", "Old description.", "# Printer warm-up\n\n1. Call `printer_status`."));
        _model.ThenText(Draft("printer-warmup", "New description."));

        await Job().RunAsync(Ct);

        string diff = Assert.Single(await AsAsync(OwnerA, () => Store().ListAsync(Ct))).Diff!;
        Assert.StartsWith("--- a/skills/printer-warmup/SKILL.md\n+++ b/skills/printer-warmup/SKILL.md\n@@", diff);
        Assert.Contains("-description: Old description.", diff);
        Assert.Contains("+description: New description.", diff);
    }

    [Theory]
    [InlineData("Printer Warmup")]
    [InlineData("1-printer")]
    public async Task InvalidName_Dropped(string name)
    {
        await SeedWarmupRunsAsync();
        _model.ThenText(Draft(name, "Warms up the printer."));

        await Job().RunAsync(Ct);

        Assert.Empty(await AsAsync(OwnerA, () => Store().ListAsync(Ct)));
    }

    [Fact]
    public async Task ToggleOff_NothingRead()
    {
        await SeedWarmupRunsAsync();
        _preferences.Stored[OwnerA] = _preferences.Stored[OwnerA] with { HelpMarshalLearn = false };

        await Job().RunAsync(Ct);

        Assert.Empty(_model.Requests);
        Assert.Equal(0, _embeddings.CallCount);
    }

    // 3.3: store actions behind the API

    [Fact]
    public async Task Download_MarksDownloaded_DismissHides_OtherOwnerNotFound()
    {
        await SeedWarmupRunsAsync();
        _model.ThenText(Draft("printer-warmup", "Warms up the printer."));
        await Job().RunAsync(Ct);
        SkillProposalRecord proposal = Assert.Single(await AsAsync(OwnerA, () => Store().ListAsync(Ct)));

        Assert.Equal(ErrorType.NotFound, (await AsAsync(OwnerB, () => Store().MarkDownloadedAsync(proposal.Id, Ct))).Error.Type);
        Assert.Equal(SkillProposalStatus.Downloaded, (await AsAsync(OwnerA, () => Store().MarkDownloadedAsync(proposal.Id, Ct))).Value.Status);
        Assert.Single(await AsAsync(OwnerA, () => Store().ListAsync(Ct)));
        Assert.Equal(SkillProposalStatus.Dismissed, (await AsAsync(OwnerA, () => Store().DismissAsync(proposal.Id, Ct))).Value.Status);
        Assert.Empty(await AsAsync(OwnerA, () => Store().ListAsync(Ct)));
        Assert.Equal(ErrorType.Conflict, (await AsAsync(OwnerA, () => Store().MarkDownloadedAsync(proposal.Id, Ct))).Error.Type);
    }

    private static string Draft(string name, string description) => JsonSerializer.Serialize(new
    {
        covered = false,
        name,
        description,
        body = "# Printer warm-up\n\n1. Call `printer_status`.\n2. Call `ha_HassTurnOn` for the printer's power.\n3. Call `printer_preheat`.",
    });

    private static WorkflowRun Run(string input, string[] tools, params float[] embedding) =>
        new(RunId.New(), input, tools, [.. tools.Select(_ => (IReadOnlyList<string>)[])], embedding);

    private async Task SeedWarmupRunsAsync()
    {
        await SeedRunAsync(OwnerA, "get the printer ready", Warmup, age: TimeSpan.FromDays(3));
        await SeedRunAsync(OwnerA, "get the printer ready please", Warmup, age: TimeSpan.FromDays(2));
        await SeedRunAsync(OwnerA, "prepare the printer", Warmup, age: TimeSpan.FromDays(1));
        await SeedRunAsync(OwnerA, "summarize my mail", ["mail.search", "mail.read_message", "mail.read_message"]);
    }

    // A run with a context step holding the input and one tool call step per tool, with arguments and a result.
    private async Task<RunId> SeedRunAsync(
        OwnerId owner, string input, string[] tools, RunOutcome outcome = RunOutcome.Ok, RunRating? rating = null, TimeSpan? age = null)
    {
        var id = RunId.New();
        await using MarshalDbContext db = _database.CreateContext(owner);
        db.Runs.Add(new Run
        {
            Id = id,
            OwnerId = owner,
            Source = SessionSource.Web,
            Trigger = "user",
            StartedAt = _time.GetUtcNow() - (age ?? TimeSpan.FromHours(1)),
            Outcome = outcome,
            Rating = rating,
        });
        db.RunSteps.Add(new RunStep
        {
            Id = Guid.NewGuid(), OwnerId = owner, RunId = id, Sequence = 0, Kind = RunStepKind.Context,
            Payload = JsonSerializer.Serialize(new { input, content = "system prompt" }),
        });
        for (int index = 0; index < tools.Length; index++)
        {
            object arguments = index == 1 ? new { name = "Voron power", domain = "switch" } : new { printer = "voron" };
            db.RunSteps.Add(new RunStep
            {
                Id = Guid.NewGuid(), OwnerId = owner, RunId = id, Sequence = index + 1, Kind = RunStepKind.ToolCall, Tool = tools[index],
                Payload = JsonSerializer.Serialize(new { arguments = JsonSerializer.Serialize(arguments), content = "Bed at 21 °C, extruder at 23 °C." }),
            });
        }

        await db.SaveChangesAsync(Ct);
        return id;
    }

    private EfWorkflowRunReader Reader() => new(_database.Factory);

    private EfSkillProposalStore Store() => new(_database.Factory, _database.Accessor, _time);

    private SkillProposalJob Job() => new(
        new Directory(),
        _preferences,
        Reader(),
        Store(),
        _embeddings,
        new BackgroundModelCalls(new StandardRouter(_model), _recorded, new FixedCost(), new FixedCap(), _database.Accessor, _time),
        _skills,
        _sender,
        _database.Accessor,
        _time,
        NullLogger<SkillProposalJob>.Instance);

    private async Task<T> AsAsync<T>(OwnerId owner, Func<Task<T>> action)
    {
        using IDisposable scope = _database.Accessor.Use(Text(owner));
        return await action();
    }

    private async Task AsAsync(OwnerId owner, Func<Task> action)
    {
        using IDisposable scope = _database.Accessor.Use(Text(owner));
        await action();
    }

    private sealed class Directory : IUserDirectory
    {
        public Task<IReadOnlyList<OwnerId>> ListAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<OwnerId>>([OwnerA, OwnerB]);
    }

    private sealed class StandardRouter(IChatClient client) : IModelRouter
    {
        public Result<RoutedModel> Route(ModelRequest request) =>
            request.Tier == ModelTier.Standard ? new RoutedModel("standard-model", ModelTier.Standard, client) : new Error("model.not_available", "No model.", ErrorType.Unavailable);
    }

    private sealed class FixedCost : ICostCalculator
    {
        public decimal Calculate(string modelKey, UsageDetails? usage) => 0.01m;
    }

    private sealed class FixedCap : IDailyCostCap
    {
        public Task<decimal> LeftTodayAsync(OwnerId owner, CancellationToken cancellationToken) => Task.FromResult(1m);
    }
}
