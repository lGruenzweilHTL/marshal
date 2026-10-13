using Marshal.Abstractions;
using Marshal.Abstractions.Printers;

namespace Marshal.Demo.Printers;

/// <summary>The simulated webcam: the stream is one current picture (a browser shows it like a still frame).</summary>
public sealed class DemoPrinterWebcams(DemoWorld world) : IPrinterWebcams
{
    public Task<Result<PrinterWebcam>> FindAsync(string printer, CancellationToken cancellationToken) =>
        Task.FromResult<Result<PrinterWebcam>>(Known(printer)
            ? new PrinterWebcam("Chamber", new Uri(DemoServiceCollectionExtensions.DemoUrl, "webcam/snapshot"), null)
            : PrinterErrors.NotFound(printer));

    public Task<Result<WebcamFeed>> OpenStreamAsync(string printer, CancellationToken cancellationToken)
    {
        if (!Known(printer))
            return Task.FromResult<Result<WebcamFeed>>(PrinterErrors.NotFound(printer));

        PrinterState state = world.Printer.State();
        return Task.FromResult<Result<WebcamFeed>>(new WebcamFeed("image/jpeg", new MemoryStream(DemoPrinterImage.Jpeg(state.Progress, state.PrintState))));
    }

    private static bool Known(string printer) => string.Equals(printer, DemoPrinter.Name, StringComparison.OrdinalIgnoreCase);
}
