using Marshal.Abstractions.Memory;
using Marshal.Abstractions.Sessions;
using Marshal.Data;
using Marshal.Data.Memory;
using Marshal.Memory.Projects;
using Marshal.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Marshal.Memory.Tests.Projects;

public sealed class ProjectActivatorTests : IAsyncLifetime
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 28, 8, 0, 0, TimeSpan.Zero));
    private readonly FakeEmbeddingGenerator _embeddings = new();
    private readonly MemoryOptions _options = new();
    private readonly ProjectSessions _sessions = new();
    private readonly SessionId _session = SessionId.New();
    private TestDatabase _database = null!;
    private ProjectActivator _activator = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _database = await TestDatabase.CreateAsync();
        _database.Accessor.Current = TestExecutionContext.Text();
        _activator = ProjectTestKit.Activator(_database, _embeddings, _sessions, _options, _time);
    }

    public async ValueTask DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task Name_ActivatesStickilyWithReasonName()
    {
        await AddProjectAsync("Voron", Axis(0));

        ProjectActivation activation = Assert.Single(await TurnAsync("How is the Voron doing?", Axis(9)));

        Assert.Equal(("Voron", ActivationReason.Name, true), (activation.Name, activation.Reason, activation.Sticky));
    }

    [Fact]
    public async Task TaskProject_AlwaysActiveWithReasonTask()
    {
        ProjectId voron = await AddProjectAsync("Voron", Axis(0));
        await AddProjectAsync("Marshal", Axis(1));

        ProjectActivation activation = Assert.Single(await _activator.ActivateAsync(new ActivationInput("Unrelated words", Axis(9), null, Pinned: voron), Ct));

        Assert.Equal((voron, ActivationReason.Task, false), (activation.ProjectId, activation.Reason, activation.Sticky));
    }

    [Fact]
    public async Task ActiveAlias_ActivatesWithReasonAlias_CandidateDoesNot()
    {
        ProjectId marshal = await AddProjectAsync("Marshal", Axis(0));
        await AddAliasAsync(marshal, "the voice assistant", ProjectAliasStatus.Active);
        await AddAliasAsync(marshal, "the helper", ProjectAliasStatus.Candidate);

        ProjectActivation alias = Assert.Single(await TurnAsync("Is the voice assistant up?", Axis(9)));
        IReadOnlyList<ProjectActivation> candidate = await TurnAsync("Ask the helper", Axis(9), session: null);

        Assert.Equal((marshal, ActivationReason.Alias), (alias.ProjectId, alias.Reason));
        Assert.Empty(candidate);
    }

    [Fact]
    public async Task Centroid_HighSticky_MediumThisTurnOnly_LowNothing()
    {
        await AddProjectAsync("Voron", Axis(0));

        ProjectActivation high = Assert.Single(await TurnAsync("high", Mix(0.9)));
        Assert.Equal((ActivationReason.Centroid, true), (high.Reason, high.Sticky));
        Assert.Empty(await TurnAsync("low", Mix(0.3), session: SessionId.New()));

        var other = SessionId.New();
        ProjectActivation medium = Assert.Single(await TurnAsync("medium", Mix(0.65), other));
        Assert.Equal((ActivationReason.Centroid, false), (medium.Reason, medium.Sticky));
        Assert.Empty(await TurnAsync("next", Mix(0.2), other));
    }

    [Fact]
    public async Task NegativeExample_SimilarToRejectedPhrase_NotActivated()
    {
        ProjectId voron = await AddProjectAsync("Voron", Axis(0));
        await ProjectTestKit.Learning(_database, _options, _time).RecordAsync(voron, ProjectFeedbackKind.Rejected, "print my tax form", Mix(0.7), null, Ct);

        Assert.Empty(await TurnAsync("print my tax form", Mix(0.7), session: null));
        Assert.Single(await TurnAsync("the printer", Axis(0), session: null));
    }

    [Fact]
    public async Task RecencyPrior_RecentProjectCrossesMediumWithReasonRecency()
    {
        await AddProjectAsync("Voron", Axis(0), lastActive: _time.GetUtcNow().AddHours(-1));
        await AddProjectAsync("Garden", Axis(5));

        ProjectActivation activation = Assert.Single(await TurnAsync("almost", Mix(0.58), session: null));

        Assert.Equal(ActivationReason.Recency, activation.Reason);
    }

    [Fact]
    public async Task Sticky_DecaysOverTurnsAndDrops()
    {
        await AddProjectAsync("Voron", Axis(0));
        await TurnAsync("high", Mix(0.9));

        List<bool> active = [];
        for (int turn = 0; turn < 4; turn++)
            active.Add((await TurnAsync($"follow-up {turn}", Mix(0.1))).Any(activation => activation.Reason == ActivationReason.Sticky));

        Assert.Equal([true, true, true, false], active);
    }

    [Fact]
    public async Task Sticky_TopicShift_Dropped()
    {
        await AddProjectAsync("Voron", Axis(0));
        await TurnAsync("high", Mix(0.9));

        Assert.Empty(await TurnAsync("weather", Axis(12)));
    }

    [Fact]
    public async Task Centroid_UsesDescriptionAndTopChunks_RecomputedAfterChunkWrite()
    {
        ProjectId voron = await AddProjectAsync("Voron", Axis(0));
        Assert.Empty(await TurnAsync("chunk topic", Axis(6), session: null));
        SqliteMemoryStore store = MemoryTestKit.Store(_database, _embeddings, _time);
        _embeddings.Pin("Klipper\nThe firmware is Klipper.", Axis(6));
        await store.WriteAsync(new ChunkDraft(ChunkScope.Project, voron, ChunkType.Fact, "Klipper", "The firmware is Klipper.", 5), ChunkAuthor.User, Ct);

        ProjectActivation activation = Assert.Single(await TurnAsync("chunk topic", Axis(6), session: null));

        Assert.Equal(ActivationReason.Centroid, activation.Reason);
        Assert.Equal(Math.Round(Math.Sqrt(0.5), 3), activation.Score);
    }

    private Task<IReadOnlyList<ProjectActivation>> TurnAsync(string text, float[] embedding) => TurnAsync(text, embedding, _session);

    private Task<IReadOnlyList<ProjectActivation>> TurnAsync(string text, float[] embedding, SessionId? session) =>
        _activator.ActivateAsync(new ActivationInput(text, embedding, session), Ct);

    private async Task<ProjectId> AddProjectAsync(string name, float[] centroid, DateTimeOffset? lastActive = null)
    {
        _embeddings.Pin($"{name}\n{name} project", centroid);
        ProjectId id = await MemoryTestKit.AddProjectAsync(_database, name, $"{name} project");
        if (lastActive is not null)
        {
            await using MarshalDbContext db = _database.CreateContext();
            await db.Projects.Where(project => project.Id == id).ExecuteUpdateAsync(update => update.SetProperty(project => project.LastActiveAt, lastActive), Ct);
        }

        return id;
    }

    private async Task AddAliasAsync(ProjectId project, string phrase, ProjectAliasStatus status)
    {
        await using MarshalDbContext db = _database.CreateContext();
        db.ProjectAliases.Add(new ProjectAlias
        {
            Id = Guid.CreateVersion7(),
            OwnerId = TestExecutionContext.OwnerA,
            ProjectId = project,
            Phrase = phrase,
            Status = status,
            CreatedAt = _time.GetUtcNow(),
        });
        await db.SaveChangesAsync(Ct);
    }

    internal static float[] Axis(int axis)
    {
        float[] vector = new float[16];
        vector[axis] = 1;
        return vector;
    }

    /// <summary>cos(θ) on axis 0 and sin(θ) on a second axis: cosine <paramref name="cosine"/> to <see cref="Axis"/>(0).</summary>
    internal static float[] Mix(double cosine, int secondAxis = 1)
    {
        float[] vector = new float[16];
        vector[0] = (float)cosine;
        vector[secondAxis] = (float)Math.Sqrt(1 - (cosine * cosine));
        return vector;
    }
}
