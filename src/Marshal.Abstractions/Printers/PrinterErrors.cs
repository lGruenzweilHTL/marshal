namespace Marshal.Abstractions.Printers;

/// <summary>Stable printer error codes.</summary>
public static class PrinterErrors
{
    public static Error NotFound(string printer) => new("printer.not_found", $"There is no printer '{printer}'.", ErrorType.NotFound);

    public static Error Offline(string printer) => new("printer.offline", $"The printer '{printer}' is offline.", ErrorType.Unavailable);
}
