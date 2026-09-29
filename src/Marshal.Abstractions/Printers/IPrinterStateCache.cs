namespace Marshal.Abstractions.Printers;

/// <summary>
/// Live printer state kept from Moonraker's websocket. Status tools and the printer page read it.
/// </summary>
public interface IPrinterStateCache
{
    /// <summary>
    /// The printer's current state; <c>printer.not_found</c> for an unknown name, <c>printer.offline</c> while the
    /// websocket is disconnected.
    /// </summary>
    Result<PrinterState> Get(string printer);

    /// <summary>
    /// Calls <paramref name="changed"/> with the printer name and its new state after every change, and with a null
    /// state when the printer goes offline, until the subscription is disposed.
    /// </summary>
    IDisposable Subscribe(Action<string, PrinterState?> changed);
}
