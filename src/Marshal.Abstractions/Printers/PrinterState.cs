namespace Marshal.Abstractions.Printers;

/// <summary>
/// Live state of a printer from Moonraker's websocket subscription, as one immutable snapshot.
/// </summary>
/// <param name="Printer">Configured printer name.</param>
/// <param name="KlippyState">Klipper host state: <c>ready</c>, <c>startup</c>, <c>shutdown</c>, <c>error</c> or <c>disconnected</c>.</param>
/// <param name="StateMessage">Klipper's state message (e.g. the shutdown reason).</param>
/// <param name="PrintState">Job state: <c>standby</c>, <c>printing</c>, <c>paused</c>, <c>complete</c>, <c>cancelled</c> or <c>error</c>.</param>
/// <param name="Filename">File being printed, if any.</param>
/// <param name="Progress">Print progress 0–1.</param>
/// <param name="PrintDuration">Seconds spent printing.</param>
/// <param name="TotalDuration">Seconds since the job started, pauses included.</param>
/// <param name="FilamentUsed">Filament used in mm.</param>
/// <param name="CurrentLayer">Current layer, when the slicer reports layers.</param>
/// <param name="TotalLayers">Layer count, when the slicer reports layers.</param>
/// <param name="EtaSeconds">Estimated seconds left (from print duration and progress), while printing.</param>
/// <param name="Message">Job or display message, e.g. an error text.</param>
/// <param name="Extruder">Hotend temperature.</param>
/// <param name="Bed">Bed temperature.</param>
/// <param name="UpdatedAt">When the snapshot was built.</param>
public sealed record PrinterState(
    string Printer,
    string KlippyState,
    string? StateMessage,
    string PrintState,
    string? Filename,
    double Progress,
    double PrintDuration,
    double TotalDuration,
    double FilamentUsed,
    int? CurrentLayer,
    int? TotalLayers,
    double? EtaSeconds,
    string? Message,
    HeaterState Extruder,
    HeaterState Bed,
    DateTimeOffset UpdatedAt);

/// <summary>A heater reading.</summary>
/// <param name="Temperature">Current temperature in °C.</param>
/// <param name="Target">Target temperature in °C (0 = off).</param>
/// <param name="Power">Heater power 0–1.</param>
public sealed record HeaterState(double Temperature, double Target, double Power);
