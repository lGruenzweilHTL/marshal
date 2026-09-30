using Marshal.Abstractions;
using Marshal.Abstractions.Printers;

namespace Marshal.Api.Printers;

/// <summary>
/// A printer page control. <c>action</c> names the command; the other fields are its arguments.
/// </summary>
/// <param name="Action">start_print, pause, resume, cancel, emergency_stop, set_temperature, home, move, run_macro,
/// queue_add, queue_remove, queue_clear, queue_pause, queue_start or queue_jump.</param>
/// <param name="File">File for start_print.</param>
/// <param name="Heater">extruder or bed, for set_temperature.</param>
/// <param name="Target">Target °C for set_temperature.</param>
/// <param name="Axes">x, y and / or z for home (all when empty).</param>
/// <param name="Axis">x, y or z for move.</param>
/// <param name="Distance">mm for move.</param>
/// <param name="Macro">Allowlisted macro for run_macro.</param>
/// <param name="Files">Files for queue_add.</param>
/// <param name="JobIds">Jobs for queue_remove.</param>
/// <param name="JobId">Job for queue_jump.</param>
public sealed record PrinterControlRequest(
    string Action,
    string? File = null,
    string? Heater = null,
    double? Target = null,
    IReadOnlyList<string>? Axes = null,
    string? Axis = null,
    double? Distance = null,
    string? Macro = null,
    IReadOnlyList<string>? Files = null,
    IReadOnlyList<string>? JobIds = null,
    string? JobId = null)
{
    public Result<PrinterCommand> ToCommand() => Action switch
    {
        "start_print" => Required(File, "file", file => new PrinterCommand.StartPrint(file)),
        "pause" => new PrinterCommand.Pause(),
        "resume" => new PrinterCommand.Resume(),
        "cancel" => new PrinterCommand.Cancel(),
        "emergency_stop" => new PrinterCommand.EmergencyStop(),
        "set_temperature" => Heater switch
        {
            "extruder" when Target is { } target => new PrinterCommand.SetTemperature(PrinterHeater.Extruder, target),
            "bed" when Target is { } target => new PrinterCommand.SetTemperature(PrinterHeater.Bed, target),
            _ => PrinterErrors.Invalid("set_temperature needs heater (extruder or bed) and target."),
        },
        "home" => ParseAxes(Axes ?? []) is { } axes ? new PrinterCommand.Home(axes) : PrinterErrors.Invalid("axes may only contain x, y and z."),
        "move" => ParseAxis(Axis) is { } axis && Distance is { } distance
            ? new PrinterCommand.Move(axis, distance)
            : PrinterErrors.Invalid("move needs axis (x, y or z) and distance."),
        "run_macro" => Required(Macro, "macro", macro => new PrinterCommand.RunMacro(macro)),
        "queue_add" => new PrinterCommand.QueueAdd(Files ?? []),
        "queue_remove" => new PrinterCommand.QueueRemove(JobIds ?? []),
        "queue_clear" => new PrinterCommand.QueueClear(),
        "queue_pause" => new PrinterCommand.QueuePause(),
        "queue_start" => new PrinterCommand.QueueStart(),
        "queue_jump" => Required(JobId, "jobId", id => new PrinterCommand.QueueJump(id)),
        _ => PrinterErrors.Invalid($"Unknown action '{Action}'."),
    };

    private static Result<PrinterCommand> Required(string? value, string name, Func<string, PrinterCommand> create) =>
        string.IsNullOrWhiteSpace(value) ? PrinterErrors.Invalid($"{name} is required.") : create(value);

    private static List<PrinterAxis>? ParseAxes(IReadOnlyList<string> axes)
    {
        var parsed = new List<PrinterAxis>();
        foreach (string axis in axes)
        {
            if (ParseAxis(axis) is not { } value)
                return null;
            parsed.Add(value);
        }

        return parsed;
    }

    private static PrinterAxis? ParseAxis(string? axis) => axis?.ToLowerInvariant() switch
    {
        "x" => PrinterAxis.X,
        "y" => PrinterAxis.Y,
        "z" => PrinterAxis.Z,
        _ => null,
    };
}

/// <summary>Outcome of a control: <c>done</c>, or <c>confirmation_required</c> with a token for the dialog.</summary>
/// <param name="Status">done or confirmation_required.</param>
/// <param name="Description">What the control does, for the dialog.</param>
/// <param name="Token">Single-use confirmation token (confirm tier only).</param>
/// <param name="ExpiresAt">When the token expires.</param>
public sealed record PrinterControlResult(string Status, string Description, string? Token = null, DateTimeOffset? ExpiresAt = null);

/// <summary>Confirms a frozen control.</summary>
public sealed record PrinterControlConfirmation(string Token);

/// <summary>A printer with its live state (null while offline), its macros and its own web UI, if configured.</summary>
public sealed record PrinterSummary(string Name, bool Online, PrinterState? State, IReadOnlyList<string> Macros, Uri? WebUrl, BabysitterStatus? Babysitter);

/// <summary>A stored snapshot.</summary>
public sealed record PrinterSnapshotInfo(string Id, string Url, int Width, int Height, DateTimeOffset CapturedAt);
