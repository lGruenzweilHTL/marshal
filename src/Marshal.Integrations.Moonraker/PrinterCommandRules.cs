using System.Globalization;
using System.Text.RegularExpressions;
using Marshal.Abstractions;
using Marshal.Abstractions.Printers;

namespace Marshal.Integrations.Moonraker;

/// <summary>
/// Argument limits for printer commands and the G-code Marshal builds from them. Only these scripts ever reach
/// <c>/printer/gcode/script</c>; nothing the model or the UI writes is sent as G-code.
/// </summary>
internal static partial class PrinterCommandRules
{
    /// <summary>Upper bound for hotend targets; Klipper's own <c>max_temp</c> still applies.</summary>
    public const double MaxExtruderTarget = 350;

    /// <summary>Upper bound for bed targets; Klipper's own <c>max_temp</c> still applies.</summary>
    public const double MaxBedTarget = 150;

    /// <summary>Largest relative move in mm.</summary>
    public const double MaxMove = 300;

    public static Result Validate(PrinterCommand command, PrinterInfo printer) => command switch
    {
        PrinterCommand.StartPrint start => ValidFile(start.File) ? Result.Success() : PrinterErrors.Invalid("file must be a path relative to the gcodes folder."),
        PrinterCommand.SetTemperature { Target: var target } heat when !double.IsFinite(target) || target < 0 || target > Max(heat.Heater) =>
            PrinterErrors.Invalid($"target must be between 0 and {Max(heat.Heater).ToString(CultureInfo.InvariantCulture)} °C."),
        PrinterCommand.Move { Distance: var distance } when !double.IsFinite(distance) || distance == 0 || Math.Abs(distance) > MaxMove =>
            PrinterErrors.Invalid($"distance must be non-zero and at most {MaxMove.ToString(CultureInfo.InvariantCulture)} mm either way."),
        PrinterCommand.RunMacro macro => ValidMacro(macro.Name, printer),
        PrinterCommand.QueueAdd add when add.Files.Count == 0 || !add.Files.All(ValidFile) => PrinterErrors.Invalid("files must be paths relative to the gcodes folder."),
        PrinterCommand.QueueRemove remove when remove.JobIds.Count == 0 || remove.JobIds.Any(id => !JobId().IsMatch(id)) => PrinterErrors.Invalid("job_ids must list queued job ids."),
        PrinterCommand.QueueJump jump when !JobId().IsMatch(jump.JobId) => PrinterErrors.Invalid("job_id must be a queued job id."),
        _ => Result.Success(),
    };

    /// <summary>The G-code script for a command that has one; null for commands with their own endpoint.</summary>
    public static string? Script(PrinterCommand command) => command switch
    {
        PrinterCommand.SetTemperature heat =>
            $"SET_HEATER_TEMPERATURE HEATER={(heat.Heater == PrinterHeater.Bed ? "heater_bed" : "extruder")} TARGET={Number(heat.Target)}",
        PrinterCommand.Home { Axes.Count: 0 } => "G28",
        PrinterCommand.Home home => "G28 " + string.Join(' ', home.Axes.Distinct().Select(axis => axis.ToString())),
        PrinterCommand.Move move => $"G91\nG1 {move.Axis}{Number(move.Distance)}\nG90",
        PrinterCommand.RunMacro macro => macro.Name.ToUpperInvariant(),
        _ => null,
    };

    private static Result ValidMacro(string name, PrinterInfo printer) =>
        MacroName().IsMatch(name) && printer.Macros.Contains(name.ToUpperInvariant())
            ? Result.Success()
            : PrinterErrors.MacroNotAllowed(name, printer.Macros);

    private static bool ValidFile(string file) =>
        file.Length > 0 && !file.StartsWith('/') && !file.Split('/').Contains("..") && !file.Any(char.IsControl);

    private static double Max(PrinterHeater heater) => heater == PrinterHeater.Bed ? MaxBedTarget : MaxExtruderTarget;

    private static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    [GeneratedRegex("^[A-Za-z0-9_]+$", RegexOptions.CultureInvariant)]
    private static partial Regex MacroName();

    [GeneratedRegex("^[A-Za-z0-9_-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex JobId();
}
