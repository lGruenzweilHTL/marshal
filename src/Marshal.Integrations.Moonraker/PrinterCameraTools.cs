using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Models;
using Marshal.Abstractions.Printers;
using Marshal.Abstractions.Runs;
using Marshal.Abstractions.Templates;
using Marshal.Abstractions.Tools;
using Microsoft.Extensions.AI;

namespace Marshal.Integrations.Moonraker;

/// <summary>
/// <c>printer.snapshot</c> and <c>printer.inspect(question)</c> (both safe). Both are hidden when no printer has a
/// webcam (checked at most every few minutes); <c>inspect</c> also when neither the run's tier nor <c>standard</c>
/// has a vision model. <c>inspect</c> sends the resized snapshot and the question to that vision model and records
/// the call as a model step of the run.
/// </summary>
public sealed class PrinterCameraTools(
    IPrinterDirectory directory,
    IPrinterWebcams webcams,
    ISnapshotSource snapshots,
    IModelRouter router,
    ITemplateCatalog templates,
    IRunRecorder recorder,
    ICostCalculator costs,
    TimeProvider time)
{
    private const string InspectPrompt = """
        You look at a webcam image of a 3D printer and answer the user's question in two or three short sentences.
        Mention visible problems such as spaghetti, parts detached from the bed, blobs, layer shifts or an empty bed.
        Say so when the image is too dark or blurry to tell.
        """;

    private static readonly TimeSpan _webcamCheckInterval = TimeSpan.FromMinutes(5);

    private readonly IPrinterDirectory _directory = directory;
    private readonly ISnapshotSource _snapshots = snapshots;

    private readonly ConcurrentDictionary<string, (bool HasWebcam, DateTimeOffset CheckedAt)> _webcamChecks = new(StringComparer.OrdinalIgnoreCase);

    internal IReadOnlyList<ITool> Create() => [new SnapshotTool(this), new InspectTool(this)];

    private async ValueTask<bool> AnyWebcamAsync(CancellationToken cancellationToken)
    {
        foreach (PrinterInfo printer in _directory.Printers)
        {
            if (await HasWebcamAsync(printer.Name, cancellationToken))
                return true;
        }

        return false;
    }

    private async ValueTask<bool> HasWebcamAsync(string printer, CancellationToken cancellationToken)
    {
        DateTimeOffset now = time.GetUtcNow();
        if (_webcamChecks.TryGetValue(printer, out (bool HasWebcam, DateTimeOffset CheckedAt) check) && now - check.CheckedAt < _webcamCheckInterval)
            return check.HasWebcam;

        Result<PrinterWebcam> webcam = await webcams.FindAsync(printer, cancellationToken);
        if (webcam.IsFailure && webcam.Error.Code != "printer.no_webcam")
            return true;

        _webcamChecks[printer] = (webcam.IsSuccess, now);
        return webcam.IsSuccess;
    }

    private Result<RoutedModel> VisionModel(MarshalExecutionContext context)
    {
        // Same tier choice as the agent loop: the template's tier, else fast for voice and standard otherwise.
        ModelTier tier = templates.For(context).ModelTier ?? (context.InputMode == InputMode.Voice ? ModelTier.Fast : ModelTier.Standard);
        Result<RoutedModel> routed = router.Route(new ModelRequest(tier, Vision: true));
        return routed.IsSuccess || tier == ModelTier.Standard ? routed : router.Route(new ModelRequest(ModelTier.Standard, Vision: true));
    }

    private async Task<Result<ToolOutput>> InspectAsync(PrinterInfo printer, string question, MarshalExecutionContext context, CancellationToken cancellationToken)
    {
        Result<RoutedModel> model = VisionModel(context);
        if (model.IsFailure)
            return model.Error;

        Result<PrinterSnapshot> snapshot = await _snapshots.CaptureAsync(printer.Name, cancellationToken);
        if (snapshot.IsFailure)
            return snapshot.Error;

        long started = time.GetTimestamp();
        ChatResponse response = await model.Value.Client.GetResponseAsync(
            [
                new ChatMessage(ChatRole.System, InspectPrompt),
                new ChatMessage(ChatRole.User, [new TextContent(question), new DataContent(snapshot.Value.Jpeg, "image/jpeg")]),
            ],
            new ChatOptions(),
            cancellationToken);

        if (context.RunId is { } run)
        {
            await recorder.RecordStepAsync(run, new RunStepRecord
            {
                Depth = context.Depth,
                Kind = RunStepKind.ModelCall,
                Model = model.Value.Key,
                Tier = model.Value.Tier.ToString().ToLowerInvariant(),
                TokensIn = response.Usage?.InputTokenCount,
                TokensOut = response.Usage?.OutputTokenCount,
                TokensCached = response.Usage?.CachedInputTokenCount,
                DurationMs = (int)time.GetElapsedTime(started).TotalMilliseconds,
                Cost = costs.Calculate(model.Value.Key, response.Usage),
                Content = response.Text,
            }, cancellationToken);
        }

        return new ToolOutput(response.Text);
    }

    private static string? Text(JsonElement arguments, string name) =>
        arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text ? text : null;

    private static JsonElement Schema(IPrinterDirectory directory, JsonObject properties)
    {
        properties["printer"] = new JsonObject { ["type"] = "string", ["description"] = $"Printer name, one of: {string.Join(", ", directory.Printers.Select(printer => printer.Name))}." };
        return JsonSerializer.SerializeToElement(new JsonObject { ["type"] = "object", ["properties"] = properties });
    }

    private sealed class SnapshotTool(PrinterCameraTools owner) : ITool
    {
        public ToolDescriptor Descriptor { get; } = new()
        {
            Id = new ToolId("printer", "snapshot"),
            Description = "Takes a webcam snapshot of the 3D printer and returns it as a Markdown image to show the user.",
            ParametersSchema = Schema(owner._directory, []),
            DefaultTier = RiskTier.Safe,
            Fillers = ToolFillers.Lines("Ich schaue mir den Drucker an.", "Let me look at the printer."),
        };

        public ValueTask<bool> IsAvailableAsync(MarshalExecutionContext context, CancellationToken cancellationToken) => owner.AnyWebcamAsync(cancellationToken);

        public Result Validate(JsonElement arguments)
        {
            Result<PrinterInfo> printer = owner._directory.Resolve(Text(arguments, "printer"));
            return printer.IsFailure ? printer.Error : Result.Success();
        }

        public async Task<Result<ToolOutput>> InvokeAsync(ToolInvocation invocation, CancellationToken cancellationToken)
        {
            Result<PrinterInfo> printer = owner._directory.Resolve(Text(invocation.Arguments, "printer"));
            if (printer.IsFailure)
                return printer.Error;

            Result<PrinterSnapshot> snapshot = await owner._snapshots.CaptureAsync(printer.Value.Name, cancellationToken);
            return snapshot.Map(taken => new ToolOutput($"![{taken.Printer} webcam]({taken.Url})", taken.Url));
        }
    }

    private sealed class InspectTool(PrinterCameraTools owner) : ITool
    {
        public ToolDescriptor Descriptor { get; } = new()
        {
            Id = new ToolId("printer", "inspect"),
            Description = "Looks at the 3D printer's webcam and answers a question about what is visible, e.g. whether the print looks fine.",
            ParametersSchema = Schema(owner._directory, new JsonObject { ["question"] = new JsonObject { ["type"] = "string", ["description"] = "What to look for." } }),
            DefaultTier = RiskTier.Safe,
            Fillers = ToolFillers.Lines("Ich schaue mir den Drucker an.", "Let me look at the printer."),
        };

        public async ValueTask<bool> IsAvailableAsync(MarshalExecutionContext context, CancellationToken cancellationToken) =>
            owner.VisionModel(context).IsSuccess && await owner.AnyWebcamAsync(cancellationToken);

        public Result Validate(JsonElement arguments)
        {
            Result<PrinterInfo> printer = owner._directory.Resolve(Text(arguments, "printer"));
            if (printer.IsFailure)
                return printer.Error;

            return Text(arguments, "question") is null ? PrinterErrors.Invalid("question is required.") : Result.Success();
        }

        public Task<Result<ToolOutput>> InvokeAsync(ToolInvocation invocation, CancellationToken cancellationToken)
        {
            Result valid = Validate(invocation.Arguments);
            return valid.IsFailure
                ? Task.FromResult(Result.Failure<ToolOutput>(valid.Error))
                : owner.InspectAsync(owner._directory.Resolve(Text(invocation.Arguments, "printer")).Value, Text(invocation.Arguments, "question")!, invocation.Context, cancellationToken);
        }
    }
}
