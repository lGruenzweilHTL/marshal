using Marshal.Abstractions.Evals;
using Marshal.Abstractions.Runs;
using Marshal.Core.Evals;

namespace Marshal.Core.Tests.Evals;

public sealed class EvalComparisonTests
{
    private static EvalCase Case(EvalExpectation expectation) => new()
    {
        Id = EvalCaseId.New(),
        SourceRunId = RunId.New(),
        Expectation = expectation,
        Input = "turn off the kitchen light",
        ToolCalls = [new EvalToolCall("ha.HassTurnOff", """{"name":"kitchen"}""", "Turned off", null, false)],
        Reply = "Done.",
        RecordedAt = DateTimeOffset.UnixEpoch,
        CreatedAt = DateTimeOffset.UnixEpoch,
    };

    private static ReplayResult Replay(string arguments = """{"name":"kitchen"}""", bool recorded = true, string reply = "Done.", string? error = null, string tool = "ha.HassTurnOff") =>
        new([new ReplayedCall(tool, arguments, recorded)], reply, 0.01m, error);

    [Theory]
    [InlineData(EvalExpectation.Good)]
    [InlineData(EvalExpectation.Unrated)]
    public void Good_SameCallsAndReply_Pass(EvalExpectation expectation) =>
        Assert.Equal(EvalOutcome.Pass, EvalComparison.Compare(Case(expectation), Replay()));

    [Fact]
    public void Good_OtherReply_ChangedUnlessTheJudgeAgrees()
    {
        Assert.Equal(EvalOutcome.Changed, EvalComparison.Compare(Case(EvalExpectation.Good), Replay(reply: "The kitchen light is off.")));
        Assert.Equal(EvalOutcome.Pass, EvalComparison.Compare(Case(EvalExpectation.Good), Replay(reply: "The kitchen light is off."), judgeScore: 0.9));
        Assert.Equal(EvalOutcome.Changed, EvalComparison.Compare(Case(EvalExpectation.Good), Replay(reply: "The kitchen light is off."), judgeScore: 0.4));
    }

    [Fact]
    public void UnrecordedCall_Diverged()
    {
        Assert.Equal(EvalOutcome.Diverged, EvalComparison.Compare(Case(EvalExpectation.Good), Replay("""{"name":"lamp"}""", recorded: false)));
        Assert.Equal(EvalOutcome.Diverged, EvalComparison.Compare(Case(EvalExpectation.Bad), Replay("""{"name":"lamp"}""", recorded: false)));
    }

    [Fact]
    public void Bad_RepeatsRecording_Changed_DifferentSequence_Pass()
    {
        Assert.Equal(EvalOutcome.Changed, EvalComparison.Compare(Case(EvalExpectation.Bad), Replay()));
        Assert.Equal(EvalOutcome.Pass, EvalComparison.Compare(Case(EvalExpectation.Bad), new ReplayResult([], "Which light?", 0, null)));
    }

    [Fact]
    public void Error_Failed() =>
        Assert.Equal(EvalOutcome.Failed, EvalComparison.Compare(Case(EvalExpectation.Good), Replay(error: "Error: step_limit")));
}
