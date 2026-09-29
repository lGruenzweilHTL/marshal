using Marshal.Abstractions.Memory;
using Marshal.Memory.Retrieval;

namespace Marshal.Memory.Tests.Retrieval;

public sealed class ChunkScorerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 28, 8, 0, 0, TimeSpan.Zero);
    private static readonly ProjectId Voron = ProjectId.New();

    private readonly MemoryOptions _options = new();

    [Fact]
    public void Score_SimilarityTimesImportanceTimesFreshness()
    {
        _options.Retrieval.HalfLifeDays[ChunkType.Fact] = 10;
        ChunkScorer scorer = new(Microsoft.Extensions.Options.Options.Create(_options));

        ScoredChunk scored = Assert.Single(scorer.Select([Candidate("a", ChunkType.Fact, 0.8, importance: 4, ageDays: 10)], Context()));

        Assert.Equal(0.8 * (4 / 5.0) * 0.5, scored.Score, precision: 6);
    }

    [Fact]
    public void Score_HalfLifePerType_EventsFadeFasterThanProcedures()
    {
        ChunkScorer scorer = Scorer();

        IReadOnlyList<ScoredChunk> result = scorer.Select(
            [Candidate("event", ChunkType.Event, 0.9, ageDays: 60), Candidate("procedure", ChunkType.Procedure, 0.9, ageDays: 60)], Context());

        Assert.Equal(["procedure", "event"], result.Select(chunk => chunk.Chunk.Title));
        Assert.True(result[1].Score < result[0].Score / 2);
    }

    [Fact]
    public void Select_QuotaPerType_OtherTypesFillRemainingSlots()
    {
        _options.Retrieval.Quotas[ChunkType.Event] = 5;
        ChunkScorer scorer = Scorer();
        List<ChunkCandidate> candidates =
        [
            .. Enumerable.Range(0, 20).Select(index => Candidate($"event {index}", ChunkType.Event, 0.95)),
            Candidate("oat milk", ChunkType.Preference, 0.5),
            Candidate("espresso", ChunkType.Preference, 0.4),
        ];

        IReadOnlyList<ScoredChunk> result = scorer.Select(candidates, Context());

        Assert.Equal(5, result.Count(chunk => chunk.Chunk.Type == ChunkType.Event));
        Assert.Equal(2, result.Count(chunk => chunk.Chunk.Type == ChunkType.Preference));
    }

    [Fact]
    public void Select_TokenBudget_StopsAddingChunksThatDoNotFit()
    {
        ChunkScorer scorer = Scorer();
        List<ChunkCandidate> candidates =
        [
            Candidate("big", ChunkType.Fact, 0.9, content: new string('x', 400)),
            Candidate("small", ChunkType.Fact, 0.5, content: "short"),
            Candidate("medium", ChunkType.Fact, 0.4, content: new string('y', 200)),
        ];

        IReadOnlyList<ScoredChunk> result = scorer.Select(candidates, Context(tokenBudget: 110));

        Assert.Equal(["big", "small"], result.Select(chunk => chunk.Chunk.Title));
    }

    [Fact]
    public void Score_ActiveProjectBoosted_InactivePenalizedNotExcluded()
    {
        ChunkScorer scorer = Scorer();
        ChunkCandidate projectChunk = Candidate("z offset", ChunkType.Procedure, 0.8, project: Voron);
        ChunkCandidate general = Candidate("general", ChunkType.Procedure, 0.8);

        double active = scorer.Select([projectChunk], Context(active: [Voron])).Single().Score;
        double inactive = Assert.Single(scorer.Select([projectChunk], Context())).Score;
        double neutral = scorer.Select([general], Context()).Single().Score;

        Assert.True(active > neutral);
        Assert.True(inactive < neutral);
        Assert.True(inactive > 0);
    }

    [Fact]
    public void Select_BelowMinimumScore_Dropped()
    {
        _options.Retrieval.MinScore = 0.2;
        ChunkScorer scorer = Scorer();

        IReadOnlyList<ScoredChunk> result = scorer.Select([Candidate("weak", ChunkType.Fact, 0.1), Candidate("strong", ChunkType.Fact, 0.9)], Context());

        Assert.Equal(["strong"], result.Select(chunk => chunk.Chunk.Title));
    }

    private ChunkScorer Scorer() => new(Microsoft.Extensions.Options.Options.Create(_options));

    private static ScoringContext Context(int tokenBudget = 1000, ProjectId[]? active = null) =>
        new(new HashSet<ProjectId>(active ?? []), Now, tokenBudget);

    private static ChunkCandidate Candidate(string title, ChunkType type, double similarity, int importance = 5, double ageDays = 0, ProjectId? project = null, string content = "content") =>
        new(new MemoryChunk(
            ChunkIds.Compute(Marshal.Testing.TestExecutionContext.OwnerA, project is null ? ChunkScope.General : ChunkScope.Project, project, type, title),
            project is null ? ChunkScope.General : ChunkScope.Project, project, type, title, content, importance, false, null, ChunkAuthor.Agent,
            Now.AddDays(-ageDays), Now.AddDays(-ageDays), null, null), similarity);
}
