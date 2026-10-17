using Marshal.Abstractions.Guard;
using Marshal.Abstractions.Runs;
using Marshal.Abstractions.Tools;
using Marshal.Data.Conversations;
using Marshal.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using static Marshal.Testing.TestExecutionContext;

namespace Marshal.Data.Tests;

public sealed class RecordedToolLatencyStatsTests
{
    private static readonly ToolId _snapshot = new("printer", "snapshot");

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 28, 10, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Expected_MedianOfLastTwentyCalls()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        await using MarshalDbContext context = database.CreateContext(OwnerA);
        // Five old slow calls fall out of the window of the newest twenty.
        await SeedAsync(database, [.. Enumerable.Repeat(9000, 5), .. Enumerable.Range(1, 20).Select(index => index * 100)]);

        TimeSpan? expected = await Stats(database).ExpectedDurationAsync(_snapshot, Ct);

        Assert.Equal(TimeSpan.FromMilliseconds(1050), expected);
    }

    [Fact]
    public async Task Expected_FewerThanFiveCalls_Null()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        await using MarshalDbContext context = database.CreateContext(OwnerA);
        await SeedAsync(database, [2000, 2000, 2000, 2000]);

        Assert.Null(await Stats(database).ExpectedDurationAsync(_snapshot, Ct));
    }

    [Fact]
    public async Task Expected_CachedForFiveMinutes()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        await using MarshalDbContext context = database.CreateContext(OwnerA);
        RecordedToolLatencyStats stats = Stats(database);
        await SeedAsync(database, [100, 100, 100, 100, 100]);
        TimeSpan? first = await stats.ExpectedDurationAsync(_snapshot, Ct);
        await SeedAsync(database, [.. Enumerable.Repeat(5000, 20)]);

        TimeSpan? cached = await stats.ExpectedDurationAsync(_snapshot, Ct);
        _time.Advance(TimeSpan.FromMinutes(5));
        TimeSpan? refreshed = await stats.ExpectedDurationAsync(_snapshot, Ct);

        Assert.Equal((TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(5000)), (first, cached, refreshed));
    }

    [Fact]
    public async Task Expected_PendingAndDeniedCallsIgnored()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        await using MarshalDbContext context = database.CreateContext(OwnerA);
        await SeedAsync(database, [0, 0, 0, 0, 0, 0], GuardDecision.Pending);
        await SeedAsync(database, [300, 300, 300, 300, 300]);

        Assert.Equal(TimeSpan.FromMilliseconds(300), await Stats(database).ExpectedDurationAsync(_snapshot, Ct));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private RecordedToolLatencyStats Stats(TestDatabase database) => new(database.Factory, database.Accessor, _time);

    // One run per call, each a second after the previous, so the order is the order given.
    private async Task SeedAsync(TestDatabase database, int[] durations, GuardDecision decision = GuardDecision.Safe)
    {
        var recorder = new DbRunRecorder(database.Factory, [], Options.Create(new RecordingOptions()), _time);
        foreach (int duration in durations)
        {
            RunId run = await recorder.StartRunAsync(new RunStart(Text(OwnerA), "turn"), Ct);
            await recorder.RecordStepAsync(run, new RunStepRecord { Kind = RunStepKind.ToolCall, Tool = _snapshot, GuardDecision = decision, DurationMs = duration }, Ct);
            _time.Advance(TimeSpan.FromSeconds(1));
        }
    }
}
