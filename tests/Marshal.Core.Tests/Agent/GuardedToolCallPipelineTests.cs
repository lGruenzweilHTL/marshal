using System.Text.Json;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Guard;
using Marshal.Abstractions.Runs;
using Marshal.Abstractions.Satellites;
using Marshal.Abstractions.Tasks;
using Marshal.Abstractions.Tools;
using Marshal.Core.Agent;
using Marshal.Core.Guard;
using Marshal.Core.Tools;
using Marshal.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Marshal.Core.Tests.Agent;

public sealed class GuardedToolCallPipelineTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 17, 10, 0, 0, TimeSpan.Zero));
    private readonly InMemoryRunRecorder _recorder = new();
    private InMemoryPendingActionStore _store = null!;
    private QueueingApprovals _approvals = null!;

    [Fact]
    public async Task Safe_ExecutesAndRecords()
    {
        FakeTool tool = FakeTool.Safe().Returns("21 degrees");
        (IToolCallPipeline pipeline, MarshalExecutionContext context) = await CreateAsync(TestExecutionContext.Text(), tool);
        GuardVerdict? decided = null;

        ToolCallOutcome outcome = await pipeline.CallAsync(Request("test_safe", context) with
        {
            OnDecided = (verdict, _) => { decided = verdict; return Task.CompletedTask; },
            Notes = () => [new Marshal.Abstractions.Context.ContextNote("tools", "test", "call", null)],
        }, Ct);

        Assert.Equal("21 degrees", outcome.Text);
        Assert.Equal(GuardOutcome.Allow, decided?.Outcome);
        Assert.Single(tool.Invocations);
        RunStepRecord step = Assert.Single(_recorder.OnlyRun.Steps);
        Assert.Equal((GuardDecision.Safe, "21 degrees"), (step.GuardDecision!.Value, step.Content));
        Assert.Equal("tools", Assert.Single(step.Notes).Kind);
    }

    [Fact]
    public async Task NotOffered_DeniedAndRecordedWithoutExecuting()
    {
        var tool = FakeTool.Safe();
        (IToolCallPipeline pipeline, MarshalExecutionContext context) = await CreateAsync(TestExecutionContext.Text(), tool);

        ToolCallOutcome outcome = await pipeline.CallAsync(Request("test_safe", context, offered: new HashSet<string>()), Ct);

        Assert.Equal(GuardOutcome.Deny, outcome.Verdict?.Outcome);
        Assert.Contains("\"error\"", outcome.Text, StringComparison.Ordinal);
        Assert.Empty(tool.Invocations);
        Assert.Equal(GuardDecision.Denied, Assert.Single(_recorder.OnlyRun.Steps).GuardDecision);
    }

    [Fact]
    public async Task Untrusted_TaintsRunAndWrapsResult()
    {
        (IToolCallPipeline pipeline, MarshalExecutionContext context) = await CreateAsync(TestExecutionContext.Text(), FakeTool.Untrusted().Returns("Dear user"));

        ToolCallOutcome outcome = await pipeline.CallAsync(Request("mail_read", context), Ct);

        Assert.True(context.Taint.IsTainted);
        Assert.Contains("Dear user", outcome.Text, StringComparison.Ordinal);
        Assert.NotEqual("Dear user", outcome.Text);
    }

    [Fact]
    public async Task Sensitive_OutcomeIsRedacted()
    {
        (IToolCallPipeline pipeline, MarshalExecutionContext context) = await CreateAsync(TestExecutionContext.Text(), FakeTool.Sensitive().Returns("secret note", "notes:a.md"));

        ToolCallOutcome outcome = await pipeline.CallAsync(Request("notes_read", context), Ct);

        Assert.True(outcome.Redacted);
    }

    [Fact]
    public async Task Failure_ErrorTextAndNoTaint()
    {
        (IToolCallPipeline pipeline, MarshalExecutionContext context) = await CreateAsync(TestExecutionContext.Text(), FakeTool.Untrusted().Fails("mail.down", "Mail is down."));

        ToolCallOutcome outcome = await pipeline.CallAsync(Request("mail_read", context), Ct);

        Assert.True(outcome.Result?.IsFailure);
        Assert.Contains("mail.down", outcome.Text, StringComparison.Ordinal);
        Assert.False(context.Taint.IsTainted);
    }

    [Fact]
    public async Task Crash_IsToolFailed()
    {
        FakeTool tool = FakeTool.Safe().Returns(_ => throw new InvalidOperationException("boom"));
        (IToolCallPipeline pipeline, MarshalExecutionContext context) = await CreateAsync(TestExecutionContext.Text(), tool);

        ToolCallOutcome outcome = await pipeline.CallAsync(Request("test_safe", context), Ct);

        Assert.Equal("tool.failed", outcome.Result?.Error.Code);
    }

    [Fact]
    public async Task Confirm_Interactive_FrozenIntoSession()
    {
        var tool = FakeTool.Confirm();
        (IToolCallPipeline pipeline, MarshalExecutionContext context) = await CreateAsync(TestExecutionContext.Text(), tool);

        ToolCallOutcome outcome = await pipeline.CallAsync(Request("test_confirm", context), Ct);

        Assert.NotNull(outcome.Frozen);
        Assert.False(outcome.Queued);
        Assert.Empty(_approvals.Queued);
        Assert.Empty(tool.Invocations);
        Assert.Contains("Confirmation pending", outcome.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Confirm_NonInteractiveWithoutTask_QueuedAndNobodyConfirmsInRun()
    {
        (IToolCallPipeline pipeline, MarshalExecutionContext context) = await CreateAsync(TestExecutionContext.Text() with { IsInteractive = false }, FakeTool.Confirm());

        ToolCallOutcome outcome = await pipeline.CallAsync(Request("test_confirm", context), Ct);

        Assert.True(outcome.Queued);
        Assert.Single(_approvals.Queued);
        Assert.Contains("review queue", outcome.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Confirm_NonInteractiveTaskRun_QueuedAndRunWaits()
    {
        (IToolCallPipeline pipeline, MarshalExecutionContext context) = await CreateAsync(TestExecutionContext.Text() with { IsInteractive = false, TaskId = TaskId.New() }, FakeTool.Confirm());

        ToolCallOutcome outcome = await pipeline.CallAsync(Request("test_confirm", context), Ct);

        Assert.False(outcome.Queued);
        Assert.NotNull(outcome.Frozen);
        Assert.Single(_approvals.Queued);
    }

    [Fact]
    public async Task CompleteConfirmed_RecordsConfirmTierAndTaints()
    {
        var tool = FakeTool.Untrusted();
        (IToolCallPipeline pipeline, MarshalExecutionContext context) = await CreateAsync(TestExecutionContext.Text(), tool);
        RegisteredTool registered = context.Tools!.FindByModelName("mail_read")!;

        ToolCallOutcome outcome = await pipeline.CompleteConfirmedAsync(new ConfirmedToolCall(registered, "mail_read", Arguments(), GuardDecision.ConfirmedByKeyword, new ToolOutput("Dear user"), context), Ct);

        Assert.Contains("Dear user", outcome.Text, StringComparison.Ordinal);
        Assert.True(context.Taint.IsTainted);
        RunStepRecord step = Assert.Single(_recorder.OnlyRun.Steps);
        Assert.Equal((RiskTier.Confirm, GuardDecision.ConfirmedByKeyword), (step.RiskTier!.Value, step.GuardDecision!.Value));
    }

    [Fact]
    public async Task WithoutRun_Throws()
    {
        (IToolCallPipeline pipeline, MarshalExecutionContext context) = await CreateAsync(TestExecutionContext.Text(), FakeTool.Safe());

        await Assert.ThrowsAsync<InvalidOperationException>(() => pipeline.CallAsync(Request("test_safe", context with { RunId = null }), Ct));
    }

    private async Task<(IToolCallPipeline Pipeline, MarshalExecutionContext Context)> CreateAsync(MarshalExecutionContext context, params ITool[] tools)
    {
        _store = new InMemoryPendingActionStore(_time);
        _approvals = new QueueingApprovals(_store);
        var registry = new ToolRegistry([new FakeToolSource("test", true, tools)]);
        ToolSnapshot snapshot = await registry.RefreshAsync(Ct);
        RuleBasedRiskEngine engine = GuardTestKit.Engine("risk: []");
        var satellites = new FixedSatellites("assist_satellite.kitchen", SatelliteProfile.Default);
        var visibility = new ToolVisibilityFilter(registry, engine, satellites, new FakeTemplateCatalog());
        var guard = new ToolCallGuard(registry, visibility, engine, satellites);
        var pipeline = new GuardedToolCallPipeline(
            guard, _store, _recorder, Microsoft.Extensions.Options.Options.Create(new ConfirmationOptions()), _time, NullLogger<GuardedToolCallPipeline>.Instance, _approvals);
        RunId run = await _recorder.StartRunAsync(new RunStart(context, "test"), Ct);
        return (pipeline, context with { RunId = run, Tools = snapshot });
    }

    private static ToolCallRequest Request(string name, MarshalExecutionContext context, IReadOnlySet<string>? offered = null) =>
        new(name, "call-1", Arguments(), context, offered ?? new HashSet<string>(StringComparer.Ordinal) { name });

    private static JsonElement Arguments() => JsonDocument.Parse("{}").RootElement.Clone();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
}
