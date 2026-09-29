using System.Globalization;

namespace Marshal.Abstractions.Printers;

/// <summary>
/// A printer command. The set is closed: raw G-code, restarts and file deletion do not exist.
/// </summary>
public abstract record PrinterCommand
{
    private protected PrinterCommand()
    {
    }

    /// <summary>Short English description for audit entries and confirmation prompts.</summary>
    public abstract string Describe();

    public sealed record StartPrint(string File) : PrinterCommand
    {
        public override string Describe() => $"start printing {File}";
    }

    public sealed record Pause : PrinterCommand
    {
        public override string Describe() => "pause the print";
    }

    public sealed record Resume : PrinterCommand
    {
        public override string Describe() => "resume the print";
    }

    public sealed record Cancel : PrinterCommand
    {
        public override string Describe() => "cancel the print";
    }

    public sealed record EmergencyStop : PrinterCommand
    {
        public override string Describe() => "emergency stop";
    }

    public sealed record SetTemperature(PrinterHeater Heater, double Target) : PrinterCommand
    {
        public override string Describe() => string.Create(CultureInfo.InvariantCulture, $"set the {(Heater == PrinterHeater.Bed ? "bed" : "hotend")} to {Target:0.#} °C");
    }

    /// <summary>Homes the given axes (all when empty).</summary>
    public sealed record Home(IReadOnlyList<PrinterAxis> Axes) : PrinterCommand
    {
        public override string Describe() => Axes.Count == 0 ? "home all axes" : $"home {string.Join(' ', Axes)}";
    }

    /// <summary>Moves one axis relative to its position, in mm.</summary>
    public sealed record Move(PrinterAxis Axis, double Distance) : PrinterCommand
    {
        public override string Describe() => string.Create(CultureInfo.InvariantCulture, $"move {Axis} by {Distance:0.##} mm");
    }

    public sealed record RunMacro(string Name) : PrinterCommand
    {
        public override string Describe() => $"run macro {Name}";
    }

    public sealed record QueueAdd(IReadOnlyList<string> Files) : PrinterCommand
    {
        public override string Describe() => $"queue {string.Join(", ", Files)}";
    }

    public sealed record QueueRemove(IReadOnlyList<string> JobIds) : PrinterCommand
    {
        public override string Describe() => $"remove {JobIds.Count} job(s) from the queue";
    }

    public sealed record QueueClear : PrinterCommand
    {
        public override string Describe() => "clear the job queue";
    }

    public sealed record QueuePause : PrinterCommand
    {
        public override string Describe() => "pause the job queue";
    }

    public sealed record QueueStart : PrinterCommand
    {
        public override string Describe() => "start the job queue";
    }

    public sealed record QueueJump(string JobId) : PrinterCommand
    {
        public override string Describe() => $"move job {JobId} to the front of the queue";
    }
}

/// <summary>A heater Marshal may set.</summary>
public enum PrinterHeater
{
    Extruder,
    Bed,
}

/// <summary>A motion axis Marshal may home or move.</summary>
public enum PrinterAxis
{
    X,
    Y,
    Z,
}
