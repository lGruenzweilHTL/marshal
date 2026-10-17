using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Context;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Runs;
using Marshal.Abstractions.Templates;
using Marshal.Core.AiTasks;
using Marshal.Core.Models;
using Marshal.Core.Tests.Agent;
using Marshal.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using static Marshal.Testing.TestExecutionContext;

namespace Marshal.Core.Tests.AiTasks;

public sealed class AiTaskRunnerTests
{
    private static readonly JsonElement _structure = JsonDocument.Parse("""
        {"type":"object","properties":{"visitor":{"type":"string"}},"required":["visitor"]}
        """).RootElement.Clone();

    private readonly FixedProfiles _profiles = new("de");
    private readonly FixedCap _cap = new();
    private readonly AiTaskOptions _options = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Run_NewAutomationSessionPerTask_AsTheOwner()
    {
        LoopHarness harness = await LoopHarness.CreateAsync();
        harness.Model.ThenText("first").ThenText("second");
        InMemorySessionStore sessions = Sessions(harness);

        await Runner(harness, sessions).RunAsync(Request(), Ct);
        await Runner(harness, sessions).RunAsync(Request(), Ct);

        Assert.Equal(2, sessions.All.Count);
        Assert.All(sessions.All, session => Assert.Equal((SessionSource.Automation, OwnerA), (session.Source, session.OwnerId)));
        Assert.Equal(sessions.All.Select(session => session.Id), sessions.Closed);
        MarshalExecutionContext context = harness.Recorder.Runs[0].Start.Context;
        Assert.Equal((SessionSource.Automation, InputMode.Text, false, AgentTemplate.AiTaskName), (context.Source, context.InputMode, context.IsInteractive, context.Template));
        Assert.Equal(sessions.All[0].Id, context.SessionId);
    }

    [Fact]
    public async Task Run_TriggerNamesTheTask_DataIsTheReplyString()
    {
        LoopHarness harness = await LoopHarness.CreateAsync();
        harness.Model.ThenText("The garage door is open.");

        Result<AiTaskResult> result = await Runner(harness).RunAsync(Request(), Ct);

        Assert.Equal("ai_task:garage", harness.Recorder.OnlyRun.Start.Trigger);
        Assert.Equal(JsonValueKind.String, result.Value.Data.ValueKind);
        Assert.Equal("The garage door is open.", result.Value.Data.GetString());
        Assert.Equal(harness.Recorder.OnlyRun.Id, result.Value.RunId);
    }

    [Theory]
    [InlineData("en-GB", "en")]
    [InlineData("de", "de")]
    [InlineData(null, "de")]
    [InlineData("fr", "de")]
    public async Task Run_LanguageFromRequestElseOwnerPreference(string? requested, string expected)
    {
        LoopHarness harness = await LoopHarness.CreateAsync();
        harness.Model.ThenText("ok");

        await Runner(harness).RunAsync(Request() with { Language = requested }, Ct);

        Assert.Equal(expected, harness.Recorder.OnlyRun.Start.Context.Language);
    }

    [Fact]
    public async Task Run_Timeout_CancelledWithTimeoutCode()
    {
        LoopHarness harness = await LoopHarness.CreateAsync();
        _options.TimeoutSeconds = 5;
        harness.Model.Then(async (_, _, ct) =>
        {
            harness.Time.Advance(TimeSpan.FromSeconds(6));
            await Task.Delay(Timeout.Infinite, ct);
            return new ChatResponse();
        });

        Result<AiTaskResult> result = await Runner(harness).RunAsync(Request(), Ct);

        Assert.Equal(AiTaskErrors.TimeoutCode, result.Error.Code);
        Assert.Equal(RunOutcome.Cancelled, harness.Recorder.OnlyRun.Completion?.Outcome);
    }

    [Fact]
    public async Task Run_DailyCapReached_RefusedBeforeAnyModelCall()
    {
        LoopHarness harness = await LoopHarness.CreateAsync();
        _cap.Left = 0m;

        Result<AiTaskResult> result = await Runner(harness).RunAsync(Request(), Ct);

        Assert.Equal(AiTaskErrors.DailyCapCode, result.Error.Code);
        Assert.Empty(harness.Model.Requests);
        Assert.Empty(harness.Recorder.Runs);
    }

    [Fact]
    public async Task Run_DailyCapReachedDuringTheRun_DailyCapCode()
    {
        FakeTool status = new FakeTool("printer.status").Returns("idle");
        LoopHarness harness = await LoopHarness.CreateAsync(tools: status);
        harness.Templates.Items.Add(AgentTemplate.BuiltInAiTask with { Tools = ["printer.*"] });
        harness.Routing.Models["fast"] = new ModelOptions { Price = new ModelPrice { InputPerMTok = 1_000_000m, OutputPerMTok = 0m } };
        _cap.Left = 0.5m;
        harness.Model
            .Then(new ChatResponse(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("c1", "printer_status")])) { Usage = new UsageDetails { InputTokenCount = 1 } })
            .ThenText("idle");

        Result<AiTaskResult> result = await Runner(harness).RunAsync(Request(), Ct);

        Assert.Equal(AiTaskErrors.DailyCapCode, result.Error.Code);
    }

    [Fact]
    public async Task Run_ConfirmTierToolOfTemplate_QueuedAndDataStillReturned()
    {
        var create = FakeTool.Confirm("calendar.create_event");
        LoopHarness harness = await LoopHarness.CreateAsync(tools: create);
        harness.Templates.Items.Add(AgentTemplate.BuiltInAiTask with { Tools = ["calendar.*"] });
        harness.Model.ThenToolCall("calendar_create_event", new { title = "Dentist" }).ThenText("The appointment waits for your approval.");

        Result<AiTaskResult> result = await Runner(harness).RunAsync(Request(), Ct);

        Assert.Equal("The appointment waits for your approval.", result.Value.Data.GetString());
        Assert.Single(harness.Approvals.Queued);
        Assert.Empty(create.Invocations);
    }

    [Fact]
    public async Task Run_Structure_ShapedAfterTheLoop()
    {
        LoopHarness harness = await LoopHarness.CreateAsync();
        harness.Model.ThenText("A courier is at the door.").ThenText("""{"visitor":"courier"}""");

        Result<AiTaskResult> result = await Runner(harness).RunAsync(Request() with { Structure = _structure }, Ct);

        Assert.Equal("courier", result.Value.Data.GetProperty("visitor").GetString());
        Assert.Null(harness.Model.Requests[0].Options?.ResponseFormat);
        Assert.IsType<ChatResponseFormatJson>(harness.Model.Requests[1].Options?.ResponseFormat);
        Assert.Equal(RunOutcome.Ok, harness.Recorder.OnlyRun.Completion?.Outcome);
    }

    [Fact]
    public async Task Run_StructureInvalidTwice_InvalidOutputAndRunError()
    {
        LoopHarness harness = await LoopHarness.CreateAsync();
        harness.Model.ThenText("A courier.").ThenText("{}").ThenText("[]");

        Result<AiTaskResult> result = await Runner(harness).RunAsync(Request() with { Structure = _structure }, Ct);

        Assert.Equal(StructuredOutput.InvalidOutputCode, result.Error.Code);
        Assert.Equal(RunOutcome.Error, harness.Recorder.OnlyRun.Completion?.Outcome);
    }

    [Fact]
    public async Task Run_AttachmentsWithoutVisionModel_NoVisionModelCode()
    {
        LoopHarness harness = await LoopHarness.CreateAsync();
        harness.Router.NoVision = true;

        Result<AiTaskResult> result = await Runner(harness).RunAsync(Request() with { Attachments = [new AiTaskAttachment("image/jpeg", new byte[] { 1, 2, 3 })] }, Ct);

        Assert.Equal(AiTaskErrors.NoVisionModelCode, result.Error.Code);
        Assert.Empty(harness.Model.Requests);
    }

    [Fact]
    public async Task Run_Attachments_RecordKeepsTypeSizeAndHashNotBytes()
    {
        LoopHarness harness = await LoopHarness.CreateAsync();
        harness.Model.ThenText("A parcel.");
        byte[] image = [0xFF, 0xD8, 0xFF, 0xE0, 0x42];

        await Runner(harness).RunAsync(Request() with { Attachments = [new AiTaskAttachment("image/jpeg", image)] }, Ct);

        RunStepRecord context = harness.Recorder.OnlyRun.Steps[0];
        ContextNote note = Assert.Single(context.Notes, note => note.Kind == "attachment");
        Assert.Equal("image/jpeg", note.Label);
        Assert.Equal($"5 bytes, sha256 {Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(image))}", note.Detail);
        Assert.Equal(image, harness.Model.Requests[0].Messages[^1].Contents.OfType<DataContent>().Single().Data.ToArray());
    }

    private AiTaskRunner Runner(LoopHarness harness, InMemorySessionStore? sessions = null) => new(
        harness.Runner,
        sessions ?? Sessions(harness),
        _profiles,
        _cap,
        harness.Accessor,
        Options.Create(_options));

    private static InMemorySessionStore Sessions(LoopHarness harness) => new(harness.Accessor, harness.Time);

    private static AiTaskRequest Request() => new(OwnerA, "garage", "Write a short reminder that the garage is open", null, null, []);

    private sealed class FixedProfiles(string language) : IUserProfileProvider
    {
        public Task<Result<UserProfile>> GetAsync(OwnerId owner, CancellationToken cancellationToken) =>
            Task.FromResult<Result<UserProfile>>(new UserProfile(owner, "Alice", language, "Europe/Vienna"));
    }

    private sealed class FixedCap : IDailyCostCap
    {
        public decimal Left { get; set; } = 2m;

        public Task<decimal> LeftTodayAsync(OwnerId owner, CancellationToken cancellationToken) => Task.FromResult(Left);
    }
}
