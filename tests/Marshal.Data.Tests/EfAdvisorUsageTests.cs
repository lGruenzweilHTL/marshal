using Marshal.Abstractions.Runs;
using Marshal.Data.Conversations;
using Marshal.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using static Marshal.Testing.TestExecutionContext;

namespace Marshal.Data.Tests;

public sealed class EfAdvisorUsageTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 28, 10, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task GetAdvisorCostSince_SumsAdvisorStepsOfOwnRunsSinceTime()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        var recorder = new DbRunRecorder(database.Factory, [], Options.Create(new RecordingOptions()), _time);
        database.Accessor.Current = Text(OwnerA);
        RunId yesterday = await recorder.StartRunAsync(new RunStart(Text(OwnerA), "turn"), Ct);
        await recorder.RecordStepAsync(yesterday, new RunStepRecord { Kind = RunStepKind.ModelCall, Tier = "advisor", Cost = 5m }, Ct);
        _time.Advance(TimeSpan.FromDays(1));
        RunId today = await recorder.StartRunAsync(new RunStart(Text(OwnerA), "turn"), Ct);
        await recorder.RecordStepAsync(today, new RunStepRecord { Kind = RunStepKind.ModelCall, Tier = "advisor", Cost = 0.25m }, Ct);
        await recorder.RecordStepAsync(today, new RunStepRecord { Kind = RunStepKind.ModelCall, Tier = "standard", Cost = 1m }, Ct);
        database.Accessor.Current = Text(OwnerB);
        RunId others = await recorder.StartRunAsync(new RunStart(Text(OwnerB), "turn"), Ct);
        await recorder.RecordStepAsync(others, new RunStepRecord { Kind = RunStepKind.ModelCall, Tier = "advisor", Cost = 7m }, Ct);

        database.Accessor.Current = Text(OwnerA);
        decimal cost = await new EfAdvisorUsage(database.Factory).GetAdvisorCostSinceAsync(_time.GetUtcNow().AddHours(-1), Ct);

        Assert.Equal(0.25m, cost);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
}
