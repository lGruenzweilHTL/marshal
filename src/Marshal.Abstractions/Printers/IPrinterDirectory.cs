namespace Marshal.Abstractions.Printers;

/// <summary>
/// The configured printers.
/// </summary>
public interface IPrinterDirectory
{
    IReadOnlyList<PrinterInfo> Printers { get; }

    /// <summary>
    /// The named printer, or the only one when <paramref name="printer"/> is empty. Fails with
    /// <c>printer.not_found</c> for an unknown name and <c>printer.ambiguous</c> (listing the names) when several
    /// printers exist and none is named.
    /// </summary>
    Result<PrinterInfo> Resolve(string? printer);
}

/// <summary>A configured printer.</summary>
/// <param name="Name">Name used in tools and the UI.</param>
/// <param name="Macros">Allowlisted Klipper macros (upper case).</param>
/// <param name="WebUrl">The printer's own web UI (Mainsail / Fluidd) for everything Marshal does not offer.</param>
public sealed record PrinterInfo(string Name, IReadOnlyList<string> Macros, Uri? WebUrl = null);
