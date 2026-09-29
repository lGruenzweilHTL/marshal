using System.Text.RegularExpressions;
using Marshal.Abstractions.Events;
using Marshal.Abstractions.Images;
using Marshal.Abstractions.Printers;
using Marshal.Abstractions.Tools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;

namespace Marshal.Integrations.Moonraker;

/// <summary>
/// Registers the Moonraker printer integration (<c>printers</c> and <c>printerSnapshots</c> in <c>marshal.yaml</c>).
/// Without printers there is no connection and there are no printer tools. The host provides
/// <see cref="ISnapshotLinks"/> and the snapshot directory.
/// </summary>
public static partial class PrinterServiceCollectionExtensions
{
    public static IServiceCollection AddMarshalPrinters(this IServiceCollection services, IConfiguration configuration, Func<IServiceProvider, string> snapshotDirectory)
    {
        services.AddOptions<PrinterOptions>().Bind(configuration)
            .Validate(options => options.Printers.All(printer => PrinterName().IsMatch(printer.Name)), "printers[].name must be letters, digits, '-' or '_'.")
            .Validate(options => options.Printers.Select(printer => printer.Name.ToUpperInvariant()).Distinct().Count() == options.Printers.Count, "printers[].name must be unique.")
            .Validate(options => options.Printers.All(printer => printer.Url is { IsAbsoluteUri: true } url && (url.Scheme == Uri.UriSchemeHttp || url.Scheme == Uri.UriSchemeHttps)), "printers[].url must be an absolute http(s) URL.")
            .Validate(options => options.Printers.All(printer => printer.WebUrl is null || printer.WebUrl is { IsAbsoluteUri: true } url && (url.Scheme == Uri.UriSchemeHttp || url.Scheme == Uri.UriSchemeHttps)), "printers[].webUrl must be an absolute http(s) URL.")
            .Validate(options => options.PrinterSnapshots is { MaxSize: >= 64, Quality: >= 1 and <= 100, Retention: >= 1 }, "printerSnapshots needs maxSize >= 64, quality 1-100 and retention >= 1.")
            .ValidateOnStart();

        // Commands are not idempotent (a retried start could print twice): retries only for safe methods.
        services.AddHttpClient(MoonrakerRest.HttpClientName).AddStandardResilienceHandler(options => options.Retry.DisableForUnsafeHttpMethods());
        // Webcam streams stay open for as long as the page shows them.
        services.AddHttpClient(MoonrakerRest.WebcamClientName, client => client.Timeout = Timeout.InfiniteTimeSpan);

        services.AddSingleton<IPrinterDirectory, ConfiguredPrinterDirectory>();
        services.AddSingleton<IPrinterClient, MoonrakerPrinterClient>();
        services.AddSingleton<IPrinterWebcams, MoonrakerWebcams>();
        services.AddSingleton<IImageResizer, SkiaImageResizer>();
        services.AddSingleton(provider => new SnapshotStorage(snapshotDirectory(provider)));
        services.AddSingleton<ISnapshotSource, MoonrakerSnapshotSource>();
        services.AddSingleton<IMoonrakerSocketFactory, ClientWebSocketFactory>();
        services.AddSingleton(new MoonrakerTimings());
        services.AddSingleton<MoonrakerStateCache>();
        services.AddSingleton<IPrinterStateCache>(provider => provider.GetRequiredService<MoonrakerStateCache>());
        services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<MoonrakerStateCache>());
        services.AddSingleton<MoonrakerEventSource>();
        services.AddSingleton<IEventSource>(provider => provider.GetRequiredService<MoonrakerEventSource>());
        services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<MoonrakerEventSource>());
        services.AddSingleton<PrinterCameraTools>();
        services.AddSingleton<IToolSource, PrinterToolSource>();
        return services;
    }

    [GeneratedRegex("^[A-Za-z0-9_-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex PrinterName();
}
