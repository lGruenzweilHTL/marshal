using System.Globalization;
using System.Text;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Maintenance;
using Marshal.Abstractions.Suggestions;
using Microsoft.Extensions.Options;

namespace Marshal.Tasks.Maintenance;

/// <summary>
/// <c>maintenance_counter</c>: a counter that could back a maintenance item (a configured printer's print hours, an HA
/// runtime or cycle sensor) but no item in <c>maintenance.items</c> uses it. The suggestion carries a YAML snippet for
/// the item, shown verbatim; the interval in it is a starting point the owner adjusts. Runs daily; the dedupe key is
/// the counter, and an open suggestion expires once an item uses it.
/// </summary>
public sealed class MaintenanceCounterDetector(IOptions<MaintenanceOptions> options, IEnumerable<IMaintenanceCounterSource> sources) : ISuggestionDetector
{
    public const string DetectorId = "maintenance_counter";

    public string Id => DetectorId;

    public SuggestionTriggers Triggers { get; } = new([], TimeSpan.FromDays(1));

    public async Task<IReadOnlyList<SuggestionCandidate>> EvaluateAsync(SuggestionContext context, CancellationToken cancellationToken)
    {
        var candidates = new List<SuggestionCandidate>();
        foreach (IMaintenanceCounterSource source in sources)
        {
            foreach (MaintenanceCounter counter in await source.ListCountersAsync(cancellationToken))
            {
                if (!Configured(counter.Source))
                    candidates.Add(Candidate(counter, context.Language));
            }
        }

        return candidates;
    }

    public Task<bool> StillHoldsAsync(OwnerId owner, string dedupeKey, CancellationToken cancellationToken) =>
        Task.FromResult(dedupeKey switch
        {
            _ when dedupeKey.StartsWith("printer:", StringComparison.Ordinal) => !Configured(new MaintenanceSource(Printer: dedupeKey["printer:".Length..])),
            _ when dedupeKey.StartsWith("ha:", StringComparison.Ordinal) => !Configured(new MaintenanceSource(Ha: dedupeKey["ha:".Length..])),
            _ => true,
        });

    /// <summary>Whether an item uses the counter: any item on the same printer, or on the same HA entity.</summary>
    private bool Configured(MaintenanceSource source) => options.Value.Items.Values.Any(item =>
        source.Printer is not null
            ? string.Equals(item.Source.Printer, source.Printer, StringComparison.OrdinalIgnoreCase)
            : string.Equals(item.Source.Ha, source.Ha, StringComparison.Ordinal));

    private static SuggestionCandidate Candidate(MaintenanceCounter counter, string language)
    {
        bool german = language.StartsWith("de", StringComparison.OrdinalIgnoreCase);
        string key;
        string id;
        string name;
        string yamlSource;
        decimal every;
        decimal warnBefore;
        if (counter.Source.Printer is { } printer)
        {
            key = $"printer:{printer}";
            id = $"{ItemId(printer)}_nozzle";
            name = $"{Capitalize(counter.Name)} {(german ? "Düse" : "nozzle")}";
            yamlSource = $"{{ printer: {printer}, metric: printHours }}";
            (every, warnBefore) = (300, 20);
        }
        else
        {
            string entity = counter.Source.Ha!;
            key = $"ha:{entity}";
            id = ItemId(entity[(entity.IndexOf('.', StringComparison.Ordinal) + 1)..]);
            name = counter.Name;
            yamlSource = $"{{ ha: {entity} }}";
            (every, warnBefore) = counter.Reading?.Unit == "h" ? (1000m, 50m) : (100m, 10m);
        }

        var facts = new Dictionary<string, string>
        {
            ["counter"] = counter.Name,
            ["suggested item"] = name,
            ["suggested interval"] = Format(every, counter.Reading?.Unit ?? (counter.Source.Printer is null ? null : "h")),
        };
        if (counter.Reading is { } reading)
            facts["current value"] = Format(reading.Value, reading.Unit);

        StringBuilder snippet = new StringBuilder()
            .Append("maintenance:\n")
            .Append("  items:\n")
            .Append(CultureInfo.InvariantCulture, $"    {id}:\n")
            .Append(CultureInfo.InvariantCulture, $"      name: {Quote(name)}\n")
            .Append(CultureInfo.InvariantCulture, $"      source: {yamlSource}\n")
            .Append(CultureInfo.InvariantCulture, $"      every: {every}\n")
            .Append(CultureInfo.InvariantCulture, $"      warnBefore: {warnBefore}\n");
        return new SuggestionCandidate(key, facts, Snippet: snippet.ToString());
    }

    private static string ItemId(string text)
    {
        var id = new StringBuilder();
        foreach (char c in text.ToLowerInvariant())
            id.Append(c is (>= 'a' and <= 'z') or (>= '0' and <= '9') ? c : '_');
        return id.Length == 0 || !char.IsAsciiLetterLower(id[0]) ? $"item_{id}" : id.ToString();
    }

    private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    private static string Quote(string text) => $"\"{text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"";

    private static string Format(decimal value, string? unit)
    {
        string number = value.ToString("0.#", CultureInfo.InvariantCulture);
        return unit is null ? number : $"{number} {unit}";
    }
}
