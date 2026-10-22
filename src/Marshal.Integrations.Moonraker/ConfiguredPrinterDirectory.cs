using Marshal.Abstractions;
using Marshal.Abstractions.Printers;
using Microsoft.Extensions.Options;

namespace Marshal.Integrations.Moonraker;

/// <summary>The printers from <c>printers</c> in <c>marshal.yaml</c> (names are case-insensitive).</summary>
public sealed class ConfiguredPrinterDirectory(IOptions<PrinterOptions> options) : IPrinterDirectory
{
    public IReadOnlyList<PrinterInfo> Printers { get; } =
        [.. options.Value.Printers.Where(printer => printer.Url is not null).Select(printer => new PrinterInfo(printer.Name, [.. printer.Macros.Select(macro => macro.ToUpperInvariant())], printer.WebUrl, printer.PowerEntity))];

    public Result<PrinterInfo> Resolve(string? printer)
    {
        if (string.IsNullOrWhiteSpace(printer))
        {
            return Printers.Count switch
            {
                1 => Printers[0],
                0 => PrinterErrors.NotFound(""),
                _ => PrinterErrors.Ambiguous(Printers.Select(info => info.Name)),
            };
        }

        return Printers.FirstOrDefault(info => string.Equals(info.Name, printer.Trim(), StringComparison.OrdinalIgnoreCase)) is { } found
            ? found
            : PrinterErrors.NotFound(printer);
    }
}
