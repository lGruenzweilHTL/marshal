using System.Globalization;
using System.Text.Json;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Notifications;
using Marshal.Abstractions.Printers;
using Marshal.Abstractions.Suggestions;
using Marshal.Abstractions.Tools;

namespace Marshal.Integrations.HomeAssistant.Suggestions;

/// <summary>
/// <c>print_done_away</c>: a print completed (<c>printer.complete</c>) while the owner's <c>person</c> is away. With a
/// configured <c>printers[].powerEntity</c> that is on, the suggestion proposes switching it off through
/// <c>ha.HassTurnOff</c> by the entity's name; it no longer holds once the switch is off.
/// </summary>
public sealed class PrintDoneAwayDetector(IHomeAssistantCache cache, IPresenceProvider presence, IPrinterDirectory? printers) : ISuggestionDetector
{
    public const string DetectorId = "print_done_away";

    /// <summary>The Assist intent tool the proposed action calls.</summary>
    public static readonly ToolId TurnOff = new("ha", "HassTurnOff");

    private static readonly string[] _switchDomain = ["switch"];

    public string Id => DetectorId;

    public SuggestionTriggers Triggers { get; } = new([new SuggestionEventTrigger("printer", "complete")]);

    public async Task<IReadOnlyList<SuggestionCandidate>> EvaluateAsync(SuggestionContext context, CancellationToken cancellationToken)
    {
        if (context.Event is not { Entity: { } name } @event || printers?.Resolve(name) is not { IsSuccess: true } resolved)
            return [];
        if ((await presence.GetAsync(context.Owner, cancellationToken)).State != PresenceState.Away)
            return [];

        PrinterInfo printer = resolved.Value;
        var facts = new Dictionary<string, string>
        {
            ["printer"] = printer.Name,
            ["event"] = "the print finished",
            ["owner"] = "away from home",
        };
        if (@event.Payload.ValueKind == JsonValueKind.Object && @event.Payload.TryGetProperty("filename", out JsonElement file) && file.GetString() is { Length: > 0 } filename)
            facts["file"] = filename;

        ProposedAction? action = null;
        if (printer.PowerEntity is { } power && cache.Current?.States.GetValueOrDefault(power) is { State: "on" } state)
        {
            string switchName = state.FriendlyName ?? cache.Current.Entities.GetValueOrDefault(power)?.Name ?? power;
            facts["power switch"] = $"{switchName} (on)";
            action = new ProposedAction(
                TurnOff,
                JsonSerializer.SerializeToElement(new { name = switchName, domain = _switchDomain }),
                context.Language.StartsWith("de", StringComparison.OrdinalIgnoreCase) ? $"{switchName} ausschalten" : $"Switch off {switchName}");
        }

        string key = $"{printer.Name}:{@event.Time.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture)}";
        return [new SuggestionCandidate(key, facts, action)];
    }

    public Task<bool> StillHoldsAsync(OwnerId owner, string dedupeKey, CancellationToken cancellationToken)
    {
        string name = dedupeKey[..Math.Max(0, dedupeKey.IndexOf(':', StringComparison.Ordinal))];
        bool off = printers?.Resolve(name) is { IsSuccess: true } resolved
            && resolved.Value.PowerEntity is { } power
            && cache.Current?.States.GetValueOrDefault(power) is { State: "off" };
        return Task.FromResult(!off);
    }
}
