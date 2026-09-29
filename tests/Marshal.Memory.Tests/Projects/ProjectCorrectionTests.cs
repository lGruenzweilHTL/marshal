using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Memory;
using Marshal.Abstractions.Sessions;
using Marshal.Abstractions.Tools;
using Marshal.Data;
using Marshal.Data.Memory;
using Marshal.Memory.Projects;
using Marshal.Memory.Tools;
using Marshal.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using static Marshal.Memory.Tests.Projects.ProjectActivatorTests;

namespace Marshal.Memory.Tests.Projects;

public sealed class ProjectCorrectionTests : IAsyncLifetime
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 28, 8, 0, 0, TimeSpan.Zero));
    private readonly FakeEmbeddingGenerator _embeddings = new();
    private readonly MemoryOptions _options = new();
    private readonly ProjectSessions _sessions = new();
    private readonly SessionId _session = SessionId.New();
    private TestDatabase _database = null!;
    private ProjectActivator _activator = null!;
    private MemoryCorrectProjectTool _tool = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _database = await TestDatabase.CreateAsync();
        _database.Accessor.Current = TestExecutionContext.Text();
        _activator = ProjectTestKit.Activator(_database, _embeddings, _sessions, _options, _time);
        _tool = new MemoryCorrectProjectTool(_sessions, ProjectTestKit.Learning(_database, _options, _time), new MemoryProjects(_database.Factory));
    }

    public async ValueTask DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task Descriptor_SafeAndHiddenWithoutProjects()
    {
        Assert.Equal((new ToolId("memory", "correct_project"), RiskTier.Safe), (_tool.Descriptor.Id, _tool.Descriptor.DefaultTier));
        Assert.False(await _tool.IsAvailableAsync(Context(), Ct));

        await AddProjectAsync("Voron", Axis(0));

        Assert.True(await _tool.IsAvailableAsync(Context(), Ct));
    }

    [Fact]
    public async Task Rejected_UnloadsStoresFeedbackAndActsAsNegativeExample()
    {
        ProjectId voron = await AddProjectAsync("Voron", Axis(0));
        Assert.Single(await TurnAsync("the printer thing", Mix(0.9)));
        await TurnAsync("not that project", Mix(0.85));

        Result<ToolOutput> result = await CorrectAsync(new { kind = "rejected" });

        Assert.True(result.IsSuccess);
        ProjectFeedback feedback = Assert.Single(await FeedbackAsync());
        Assert.Equal((voron, ProjectFeedbackKind.Rejected, "the printer thing"), (feedback.ProjectId, feedback.Kind, feedback.Phrase));
        Assert.Equal(Mix(0.9), Vectors.FromBlob(feedback.Embedding!));
        Assert.Empty(await TurnAsync("the printer thing", Mix(0.9)));
        Assert.Empty(await _activator.ActivateAsync(new ActivationInput("the printer thing", Mix(0.9), SessionId.New()), Ct));
    }

    [Fact]
    public async Task Switched_OtherProjectStickyAndFeedbackRows()
    {
        ProjectId voron = await AddProjectAsync("Voron", Axis(0));
        ProjectId marshal = await AddProjectAsync("Marshal", Axis(5));
        await TurnAsync("the assistant thing", Mix(0.9));
        await TurnAsync("that is about marshal", Mix(0.85));

        Result<ToolOutput> result = await CorrectAsync(new { kind = "switched", project = "Marshal" });

        Assert.True(result.IsSuccess);
        List<ProjectFeedback> feedback = await FeedbackAsync();
        Assert.Contains(feedback, row => row.ProjectId == marshal && row.Kind == ProjectFeedbackKind.Switched && row.Phrase == "the assistant thing");
        Assert.Contains(feedback, row => row.ProjectId == voron && row.Kind == ProjectFeedbackKind.Rejected);
        IReadOnlyList<ProjectActivation> next = await TurnAsync("and now", Mix(0.8));
        ProjectActivation active = Assert.Single(next);
        Assert.Equal((marshal, ActivationReason.Sticky), (active.ProjectId, active.Reason));
    }

    [Fact]
    public async Task Confirmed_FeedbackAndAliasCandidateCounted()
    {
        ProjectId voron = await AddProjectAsync("Voron", Axis(0));
        await TurnAsync("the printer thing", Mix(0.9));
        await TurnAsync("yes exactly", Mix(0.85));

        Assert.True((await CorrectAsync(new { kind = "confirmed" })).IsSuccess);

        Assert.Equal(ProjectFeedbackKind.Confirmed, Assert.Single(await FeedbackAsync()).Kind);
        ProjectAlias alias = Assert.Single(await AliasesAsync());
        Assert.Equal((voron, "the printer thing", ProjectAliasStatus.Candidate, 1), (alias.ProjectId, alias.Phrase, alias.Status, alias.Confirmations));

        await TurnAsync("next", Mix(0.8));
        Assert.Equal(1, Assert.Single(await AliasesAsync()).Confirmations);
    }

    [Fact]
    public async Task UncorrectedInferences_PromoteAliasAfterThreeConfirmations()
    {
        ProjectId marshal = await AddProjectAsync("Marshal", Axis(0));
        for (int turn = 0; turn < 5; turn++)
            await TurnAsync("the voice thing", Mix(0.9));

        ProjectAlias alias = Assert.Single(await AliasesAsync());
        Assert.Equal((ProjectAliasStatus.Active, 3), (alias.Status, alias.Confirmations));
        ProjectActivation activation = Assert.Single(await _activator.ActivateAsync(new ActivationInput("Is the voice thing running?", Axis(9), null), Ct));
        Assert.Equal((marshal, ActivationReason.Alias), (activation.ProjectId, activation.Reason));
    }

    [Fact]
    public async Task Correct_WithoutSessionOrInference_Errors()
    {
        await AddProjectAsync("Voron", Axis(0));

        Result<ToolOutput> noSession = await _tool.InvokeAsync(new ToolInvocation(Json(new { kind = "rejected" }), TestExecutionContext.Text()), Ct);
        Result<ToolOutput> nothing = await CorrectAsync(new { kind = "rejected" });
        Result<ToolOutput> switchedWithoutProject = await CorrectAsync(new { kind = "switched" });

        Assert.Equal("memory.no_session", noSession.Error.Code);
        Assert.Equal("memory.nothing_to_correct", nothing.Error.Code);
        Assert.Equal("memory.invalid_arguments", switchedWithoutProject.Error.Code);
    }

    private MarshalExecutionContext Context() => TestExecutionContext.Text() with { SessionId = _session };

    private Task<Result<ToolOutput>> CorrectAsync(object arguments) =>
        _tool.InvokeAsync(new ToolInvocation(Json(arguments), Context()), Ct);

    private Task<IReadOnlyList<ProjectActivation>> TurnAsync(string text, float[] embedding) =>
        _activator.ActivateAsync(new ActivationInput(text, embedding, _session), Ct);

    private async Task<ProjectId> AddProjectAsync(string name, float[] centroid)
    {
        _embeddings.Pin($"{name}\n{name} project", centroid);
        return await MemoryTestKit.AddProjectAsync(_database, name, $"{name} project");
    }

    private async Task<List<ProjectFeedback>> FeedbackAsync()
    {
        await using MarshalDbContext db = _database.CreateContext();
        return await db.ProjectFeedback.AsNoTracking().ToListAsync(Ct);
    }

    private async Task<List<ProjectAlias>> AliasesAsync()
    {
        await using MarshalDbContext db = _database.CreateContext();
        return await db.ProjectAliases.AsNoTracking().ToListAsync(Ct);
    }

    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);
}
