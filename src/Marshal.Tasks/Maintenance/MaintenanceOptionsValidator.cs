using System.Text.RegularExpressions;
using Marshal.Abstractions.Printers;
using Microsoft.Extensions.Options;

namespace Marshal.Tasks.Maintenance;

/// <summary>Fails startup with the first invalid <c>maintenance.items</c> key.</summary>
public sealed partial class MaintenanceOptionsValidator(IEnumerable<IPrinterDirectory> printers) : IValidateOptions<MaintenanceOptions>
{
    public ValidateOptionsResult Validate(string? name, MaintenanceOptions options) =>
        Error(options) is { } error ? ValidateOptionsResult.Fail(error) : ValidateOptionsResult.Success;

    private string? Error(MaintenanceOptions options)
    {
        HashSet<string> names = new(printers.SelectMany(directory => directory.Printers).Select(printer => printer.Name), StringComparer.OrdinalIgnoreCase);
        foreach ((string id, MaintenanceItemOptions item) in options.Items)
        {
            string key = $"maintenance.items.{id}";
            if (!ItemId().IsMatch(id))
                return $"{key}: an item id must match [a-z][a-z0-9_]*.";
            if (string.IsNullOrWhiteSpace(item.Name))
                return $"{key}.name is required.";

            MaintenanceSourceOptions source = item.Source;
            bool ha = !string.IsNullOrWhiteSpace(source.Ha);
            bool printer = !string.IsNullOrWhiteSpace(source.Printer);
            if (ha == printer)
                return $"{key}.source must have either ha or printer.";
            if (ha && source.Metric is not null)
                return $"{key}.source.metric belongs to a printer source.";
            if (printer && source.Metric is null)
                return $"{key}.source.metric must be printHours or filamentMeters.";
            if (printer && !names.Contains(source.Printer!))
                return $"{key}.source.printer: there is no printer '{source.Printer}'.";
            if (item.Every <= 0)
                return $"{key}.every must be a positive number.";
            if (item.WarnBefore is { } warn && (warn <= 0 || warn >= item.Every))
                return $"{key}.warnBefore must be positive and less than every.";
        }

        return null;
    }

    [GeneratedRegex("^[a-z][a-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex ItemId();
}
