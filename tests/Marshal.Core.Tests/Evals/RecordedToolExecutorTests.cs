using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Evals;
using Marshal.Abstractions.Tools;
using Marshal.Core.Evals;

namespace Marshal.Core.Tests.Evals;

public sealed class RecordedToolExecutorTests
{
    private static readonly ToolId _turnOff = new("ha", "HassTurnOff");

    private static JsonElement Args(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static RecordedToolExecutor Executor() => new(
    [
        new EvalToolCall("ha.HassTurnOff", CanonicalJson.Of("""{"name":"light","area":"kitchen"}"""), "Turned off", null, false),
        new EvalToolCall("printer.status", CanonicalJson.Of("{}"), null, "printer.offline: Offline.", false),
    ]);

    [Fact]
    public void MatchingCall_GetsRecordedResult_ArgumentOrderIgnored()
    {
        RecordedToolExecutor executor = Executor();

        Result<ToolOutput> result = executor.Execute(_turnOff, Args("""{"area":"kitchen","name":"light"}"""));

        Assert.Equal("Turned off", result.Value.Content);
        Assert.Equal([new ReplayedCall("ha.HassTurnOff", """{"area":"kitchen","name":"light"}""", true)], executor.Calls);
    }

    [Fact]
    public void RepeatedCall_AllowedAndRecordedFailureReplayed()
    {
        RecordedToolExecutor executor = Executor();

        executor.Execute(_turnOff, Args("""{"name":"light","area":"kitchen"}"""));
        Assert.True(executor.Execute(_turnOff, Args("""{"name":"light","area":"kitchen"}""")).IsSuccess);
        Result<ToolOutput> failed = executor.Execute(new ToolId("printer", "status"), Args("{}"));

        Assert.Equal("printer.offline: Offline.", failed.Error.Message);
        Assert.All(executor.Calls, call => Assert.True(call.Recorded));
    }

    [Fact]
    public void UnmatchedCall_NoRecordingAndDiverged()
    {
        RecordedToolExecutor executor = Executor();

        Result<ToolOutput> result = executor.Execute(_turnOff, Args("""{"name":"lamp","area":"kitchen"}"""));

        Assert.Equal(RecordedToolExecutor.NoRecording, result.Error.Code);
        Assert.True(new ReplayResult(executor.Calls, "ok", 0, null).Diverged);
    }

    [Theory]
    [InlineData("""{"b":1,"a":2}""", """{"a":2,"b":1}""")]
    [InlineData("""{"x":1.50}""", """{"x":1.5}""")]
    [InlineData("""{"x":2.0,"y":[{"d":1,"c":0}]}""", """{"x":2,"y":[{"c":0,"d":1}]}""")]
    public void CanonicalJson_EqualForEquivalentArguments(string left, string right) =>
        Assert.Equal(CanonicalJson.Of(left), CanonicalJson.Of(right));
}
