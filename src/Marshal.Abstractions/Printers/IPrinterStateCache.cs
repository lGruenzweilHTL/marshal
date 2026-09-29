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

    /// <summary>Calls <paramref name="changed"/> after every state change until the subscription is disposed.</summary>
    IDisposable Subscribe(Action<PrinterState> changed);
}
