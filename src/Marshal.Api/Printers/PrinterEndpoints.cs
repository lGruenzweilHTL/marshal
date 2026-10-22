using System.Security.Claims;
using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Guard;
using Marshal.Abstractions.Audit;
using Marshal.Abstractions.Printers;
using Marshal.Abstractions.Tools;
using Marshal.Api.Auth;
using Marshal.Api.Problems;
using Marshal.Api.Widgets;
using Marshal.Core.Agent;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Marshal.Api.Printers;

/// <summary>
/// The printer page: live status, files, queue, history, controls, snapshots and the webcam stream, all through
/// Marshal's origin. Controls use the tools' tiers: pause and emergency stop act at once, everything else returns a
/// single-use token the confirmation dialog sends back. Every executed control is audited with the user as actor.
/// </summary>
internal static class PrinterEndpoints
{
    public static RouteGroupBuilder MapPrinterEndpoints(this RouteGroupBuilder group)
    {
        RouteGroupBuilder printers = group.MapGroup("/printers").WithTags("Printers").RequireAuthorization(Policies.Human);
        printers.MapGet("/", List).WithName("ListPrinters")
            .WithDescription("Configured printers with their live state (null while offline), allowlisted macros and babysitter state (null when not enabled).");
        printers.MapGet("/{printer}", GetStatus).WithName("GetPrinterStatus")
            .WithDescription("Live state from the websocket cache.");
        printers.MapGet("/{printer}/files", ListFilesAsync).WithName("ListPrinterFiles")
            .WithDescription("Printable files, newest first.");
        printers.MapGet("/{printer}/queue", GetQueueAsync).WithName("GetPrinterQueue")
            .WithDescription("The job queue.");
        printers.MapGet("/{printer}/history", GetHistoryAsync).WithName("GetPrinterHistory")
            .WithDescription("Recent jobs, newest first.");
        printers.MapPost("/{printer}/controls", ControlAsync).WithName("ControlPrinter")
            .WithDescription("Runs a safe control, or freezes a confirm-tier control and returns its confirmation token.");
        printers.MapPost("/{printer}/controls/confirm", ConfirmAsync).WithName("ConfirmPrinterControl")
            .WithDescription("Runs a frozen control with the token from the confirmation dialog (single use, expires).");
        printers.MapPost("/{printer}/snapshots", CaptureAsync).WithName("CapturePrinterSnapshot")
            .WithDescription("Takes, resizes and stores a webcam snapshot.");
        printers.MapGet("/{printer}/snapshots/{id}", GetSnapshotAsync).WithName("GetPrinterSnapshot")
            .WithDescription("A stored snapshot as JPEG.");
        printers.MapGet("/{printer}/snapshots/{id}/shared", GetSharedSnapshotAsync).WithName("GetSharedPrinterSnapshot")
            .WithDescription("A stored snapshot as JPEG without signing in, with the signature of a shared link (push notification images; expires after a day).")
            .AllowAnonymous();
        printers.MapGet("/{printer}/webcam/stream", StreamAsync).WithName("StreamPrinterWebcam")
            .WithDescription("The webcam's live stream, proxied so the browser never contacts the printer.");
        return group;
    }

    private static Ok<List<PrinterSummary>> List(IPrinterDirectory directory, IPrinterStateCache states, IPrintBabysitterStatus babysitter) =>
        TypedResults.Ok(directory.Printers.Select(printer =>
        {
            Result<PrinterState> state = states.Get(printer.Name);
            return new PrinterSummary(printer.Name, state.IsSuccess, state.IsSuccess ? state.Value : null, printer.Macros, printer.WebUrl, babysitter.Get(printer.Name));
        }).ToList());

    private static Results<Ok<PrinterState>, ProblemHttpResult> GetStatus(string printer, IPrinterDirectory directory, IPrinterStateCache states)
    {
        Result<PrinterInfo> found = Find(directory, printer);
        if (found.IsFailure)
            return found.Error.ToProblem();

        Result<PrinterState> state = states.Get(found.Value.Name);
        return state.IsSuccess ? TypedResults.Ok(state.Value) : state.Error.ToProblem();
    }

    private static async Task<Results<Ok<IReadOnlyList<PrinterFile>>, ProblemHttpResult>> ListFilesAsync(string printer, IPrinterDirectory directory, IPrinterClient client, CancellationToken ct)
    {
        Result<PrinterInfo> found = Find(directory, printer);
        if (found.IsFailure)
            return found.Error.ToProblem();

        Result<IReadOnlyList<PrinterFile>> files = await client.ListFilesAsync(found.Value.Name, ct);
        return files.IsSuccess ? TypedResults.Ok(files.Value) : files.Error.ToProblem();
    }

    private static async Task<Results<Ok<JobQueueStatus>, ProblemHttpResult>> GetQueueAsync(string printer, IPrinterDirectory directory, IPrinterClient client, CancellationToken ct)
    {
        Result<PrinterInfo> found = Find(directory, printer);
        if (found.IsFailure)
            return found.Error.ToProblem();

        Result<JobQueueStatus> queue = await client.GetQueueAsync(found.Value.Name, ct);
        return queue.IsSuccess ? TypedResults.Ok(queue.Value) : queue.Error.ToProblem();
    }

    private static async Task<Results<Ok<IReadOnlyList<PrintJob>>, ProblemHttpResult>> GetHistoryAsync(
        string printer, int? limit, IPrinterDirectory directory, IPrinterClient client, CancellationToken ct)
    {
        Result<PrinterInfo> found = Find(directory, printer);
        if (found.IsFailure)
            return found.Error.ToProblem();

        Result<IReadOnlyList<PrintJob>> jobs = await client.GetHistoryAsync(found.Value.Name, Math.Clamp(limit ?? 20, 1, 100), ct);
        return jobs.IsSuccess ? TypedResults.Ok(jobs.Value) : jobs.Error.ToProblem();
    }

    private static async Task<Results<Ok<PrinterControlResult>, ProblemHttpResult>> ControlAsync(
        string printer, PrinterControlRequest request, ClaimsPrincipal user, IPrinterDirectory directory, IPrinterClient client,
        PrinterControlConfirmations confirmations, IAuditLog audit, WidgetRuns widgets, CancellationToken ct)
    {
        Result<PrinterInfo> found = Find(directory, printer);
        if (found.IsFailure)
            return found.Error.ToProblem();

        Result<PrinterCommand> command = request.ToCommand();
        if (command.IsFailure)
            return command.Error.ToProblem();

        Result valid = client.Validate(found.Value.Name, command.Value);
        if (valid.IsFailure)
            return valid.Error.ToProblem();

        if (command.Value.RequiresConfirmation)
        {
            (string token, DateTimeOffset expiresAt) = confirmations.Freeze(user.Owner(), found.Value.Name, command.Value);
            return TypedResults.Ok(new PrinterControlResult("confirmation_required", command.Value.Describe(), token, expiresAt));
        }

        return await RunAsync(found.Value.Name, command.Value, confirmed: false, request.Widget == true, user, client, audit, widgets, ct);
    }

    private static async Task<Results<Ok<PrinterControlResult>, ProblemHttpResult>> ConfirmAsync(
        string printer, PrinterControlConfirmation confirmation, ClaimsPrincipal user, IPrinterDirectory directory, IPrinterClient client,
        PrinterControlConfirmations confirmations, IAuditLog audit, WidgetRuns widgets, CancellationToken ct)
    {
        Result<PrinterInfo> found = Find(directory, printer);
        if (found.IsFailure)
            return found.Error.ToProblem();

        if (confirmations.Take(user.Owner(), found.Value.Name, confirmation.Token) is not { } command)
            return new Error("printer.confirmation_not_found", "The confirmation expired or was already used.", ErrorType.NotFound).ToProblem();

        return await RunAsync(found.Value.Name, command, confirmed: true, confirmation.Widget == true, user, client, audit, widgets, ct);
    }

    private static async Task<Results<Ok<PrinterControlResult>, ProblemHttpResult>> RunAsync(
        string printer, PrinterCommand command, bool confirmed, bool widget, ClaimsPrincipal user, IPrinterClient client, IAuditLog audit, WidgetRuns widgets, CancellationToken ct)
    {
        Result sent;
        if (widget)
        {
            Result<bool> run = await widgets.RunAsync(user, "printer", context => SendAsync(context, printer, command, confirmed, client, widgets, ct), ct);
            sent = run.IsSuccess ? Result.Success() : run.Error;
        }
        else
        {
            sent = await client.SendAsync(printer, command, ct);
        }
        (AuditActorKind kind, string actor) = user.Actor();
        await audit.RecordAsync(new AuditEntry(kind, actor, "printer.control", printer, new
        {
            command = command.Describe(),
            confirmed,
            outcome = sent.IsSuccess ? "ok" : sent.Error.Code,
        }), ct);
        return sent.IsSuccess ? TypedResults.Ok(new PrinterControlResult("done", command.Describe())) : sent.Error.ToProblem();
    }

    // The widget's control as a tool step: the user confirmed confirm-tier controls in the widget's dialog.
    private static async Task<Result<bool>> SendAsync(MarshalExecutionContext context, string printer, PrinterCommand command, bool confirmed, IPrinterClient client, WidgetRuns widgets, CancellationToken ct)
    {
        Result sent = await client.SendAsync(printer, command, ct);
        JsonElement arguments = JsonSerializer.SerializeToElement(new { printer });
        await widgets.RecordAsync(context, ToolCallSteps.Step(
            null, $"printer_{ToolName(command)}", arguments, confirmed ? RiskTier.Confirm : RiskTier.Safe, confirmed ? GuardDecision.ConfirmedInUi : GuardDecision.Safe, [],
            sent.IsSuccess ? new ToolOutput($"Done: {command.Describe()} on {printer}.") : null, sent.IsFailure ? sent.Error : null, 0), ct);
        return sent.IsSuccess ? true : sent.Error;
    }

    private static string ToolName(PrinterCommand command) => command switch
    {
        PrinterCommand.Pause => "pause",
        PrinterCommand.Resume => "resume",
        PrinterCommand.Cancel => "cancel",
        PrinterCommand.EmergencyStop => "emergency_stop",
        PrinterCommand.StartPrint => "start_print",
        PrinterCommand.SetTemperature => "set_temperature",
        PrinterCommand.Home => "home",
        PrinterCommand.Move => "move",
        PrinterCommand.RunMacro => "run_macro",
        _ => "edit_queue",
    };

    private static async Task<Results<Ok<PrinterSnapshotInfo>, ProblemHttpResult>> CaptureAsync(string printer, IPrinterDirectory directory, ISnapshotSource snapshots, CancellationToken ct)
    {
        Result<PrinterInfo> found = Find(directory, printer);
        if (found.IsFailure)
            return found.Error.ToProblem();

        Result<PrinterSnapshot> snapshot = await snapshots.CaptureAsync(found.Value.Name, ct);
        return snapshot.IsSuccess
            ? TypedResults.Ok(new PrinterSnapshotInfo(snapshot.Value.Id, snapshot.Value.Url, snapshot.Value.Width, snapshot.Value.Height, snapshot.Value.CapturedAt))
            : snapshot.Error.ToProblem();
    }

    private static async Task<Results<FileContentHttpResult, ProblemHttpResult>> GetSnapshotAsync(string printer, string id, IPrinterDirectory directory, ISnapshotSource snapshots, CancellationToken ct)
    {
        Result<PrinterInfo> found = Find(directory, printer);
        if (found.IsFailure)
            return found.Error.ToProblem();

        Result<byte[]> image = await snapshots.OpenAsync(found.Value.Name, id, ct);
        return image.IsSuccess ? TypedResults.File(image.Value, "image/jpeg") : image.Error.ToProblem();
    }

    // An invalid or expired signature looks like a missing snapshot.
    private static async Task<Results<FileContentHttpResult, ProblemHttpResult>> GetSharedSnapshotAsync(
        string printer, string id, string? signature, IPrinterDirectory directory, ApiSnapshotLinks links, ISnapshotSource snapshots, CancellationToken ct)
    {
        Result<PrinterInfo> found = Find(directory, printer);
        if (found.IsFailure || !links.IsValid(found.Value.Name, id, signature))
            return PrinterErrors.SnapshotNotFound(id).ToProblem();

        Result<byte[]> image = await snapshots.OpenAsync(found.Value.Name, id, ct);
        return image.IsSuccess ? TypedResults.File(image.Value, "image/jpeg") : image.Error.ToProblem();
    }

    private static async Task<Results<EmptyHttpResult, ProblemHttpResult>> StreamAsync(string printer, IPrinterDirectory directory, IPrinterWebcams webcams, HttpContext context)
    {
        Result<PrinterInfo> found = Find(directory, printer);
        if (found.IsFailure)
            return found.Error.ToProblem();

        CancellationToken ct = context.RequestAborted;
        Result<WebcamFeed> feed = await webcams.OpenStreamAsync(found.Value.Name, ct);
        if (feed.IsFailure)
            return feed.Error.ToProblem();

        await using WebcamFeed open = feed.Value;
        context.Response.ContentType = open.ContentType;
        context.Response.Headers.CacheControl = "no-store";
        try
        {
            await open.Body.CopyToAsync(context.Response.Body, ct);
        }
        catch (Exception exception) when (exception is OperationCanceledException or IOException)
        {
            // The page closed the stream or the webcam went away.
        }

        return TypedResults.Empty;
    }

    private static Result<PrinterInfo> Find(IPrinterDirectory directory, string printer) =>
        directory.Printers.FirstOrDefault(info => string.Equals(info.Name, printer, StringComparison.OrdinalIgnoreCase)) is { } found
            ? found
            : PrinterErrors.NotFound(printer);
}
