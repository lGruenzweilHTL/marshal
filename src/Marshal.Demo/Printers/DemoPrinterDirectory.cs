using Marshal.Abstractions;
using Marshal.Abstractions.Printers;

namespace Marshal.Demo.Printers;

/// <summary>The one simulated printer, <c>voron</c>, with its allowlisted macros.</summary>
public sealed class DemoPrinterDirectory : IPrinterDirectory
{
    private static readonly PrinterInfo _voron = new(DemoPrinter.Name, DemoPrinter.Macros);

    public IReadOnlyList<PrinterInfo> Printers { get; } = [_voron];

    public Result<PrinterInfo> Resolve(string? printer) =>
        printer is null || string.Equals(printer, DemoPrinter.Name, StringComparison.OrdinalIgnoreCase) ? _voron : PrinterErrors.NotFound(printer);
}
