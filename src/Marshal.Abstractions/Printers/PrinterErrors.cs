namespace Marshal.Abstractions.Printers;

/// <summary>Stable printer error codes.</summary>
public static class PrinterErrors
{
    public static Error NotFound(string printer) => new("printer.not_found", $"There is no printer '{printer}'.", ErrorType.NotFound);

    public static Error Ambiguous(IEnumerable<string> printers) =>
        new("printer.ambiguous", $"Several printers are configured; name one of: {string.Join(", ", printers)}.", ErrorType.Validation);

    public static Error Invalid(string message) => new("printer.invalid_command", message, ErrorType.Validation);

    public static Error MacroNotAllowed(string macro, IEnumerable<string> allowed) =>
        new("printer.macro_not_allowed", $"The macro '{macro}' is not allowed; allowed macros: {string.Join(", ", allowed)}.", ErrorType.Validation);

    public static Error Unauthorized(string printer) =>
        new("printer.unauthorized", $"The printer '{printer}' rejected Marshal's API key.", ErrorType.Unavailable);

    public static Error CommandFailed(string printer, string message) =>
        new("printer.command_failed", $"The printer '{printer}' refused: {message}", ErrorType.Failure);

    public static Error Offline(string printer) => new("printer.offline", $"The printer '{printer}' is offline.", ErrorType.Unavailable);
}
