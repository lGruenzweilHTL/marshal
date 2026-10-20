using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Printers;
using Marshal.Abstractions.Widgets;

namespace Marshal.Integrations.Moonraker;

/// <summary>
/// The <c>printer</c> chat widget after <c>printer.status</c>: the printer's name and its cached live state (null
/// while offline). The web widget then follows the printer live topic and controls the printer through the printer
/// endpoints, whose confirm-tier controls need the confirmation dialog.
/// </summary>
public sealed class PrinterWidgetData(IPrinterDirectory directory, IPrinterStateCache states) : IWidgetDataProvider
{
    public const string WidgetKind = "printer";

    public string Kind => WidgetKind;

    public Task<JsonElement?> GetAsync(WidgetRequest request, CancellationToken cancellationToken)
    {
        string? name = request.Arguments.ValueKind == JsonValueKind.Object && request.Arguments.TryGetProperty("printer", out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
        Result<PrinterInfo> printer = directory.Resolve(name);
        if (printer.IsFailure)
            return Task.FromResult<JsonElement?>(null);

        Result<PrinterState> state = states.Get(printer.Value.Name);
        return Task.FromResult<JsonElement?>(JsonSerializer.SerializeToElement(new { printer = printer.Value.Name, state = state.IsSuccess ? state.Value : null }, JsonSerializerOptions.Web));
    }
}
