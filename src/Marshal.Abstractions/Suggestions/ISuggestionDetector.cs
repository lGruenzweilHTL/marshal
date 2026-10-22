using System.Text.Json;
using Marshal.Abstractions.Events;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Tools;

namespace Marshal.Abstractions.Suggestions;

/// <summary>
/// Notices something worth suggesting from the owner's own device data (deterministic code, no model). The suggestion
/// pipeline runs it on its triggers for owners with "help Marshal learn" on and the detector not muted, drops repeats
/// within the cooldown, asks the acceptance policy whether to send, and has a model word the facts.
/// </summary>
public interface ISuggestionDetector
{
    /// <summary>Stable id (<c>[a-z][a-z0-9_]*</c>), the key of <c>suggestions.detectors.&lt;id&gt;</c>.</summary>
    string Id { get; }

    /// <summary>Events that start an evaluation, and an optional interval.</summary>
    SuggestionTriggers Triggers { get; }

    /// <summary>Candidates for the owner; <see cref="SuggestionContext.Event"/> is set when an event started it.</summary>
    Task<IReadOnlyList<SuggestionCandidate>> EvaluateAsync(SuggestionContext context, CancellationToken cancellationToken);

    /// <summary>Whether the condition behind an open suggestion still holds; one that no longer holds expires.</summary>
    Task<bool> StillHoldsAsync(OwnerId owner, string dedupeKey, CancellationToken cancellationToken) => Task.FromResult(true);
}

/// <summary>When a detector runs.</summary>
/// <param name="Events">Events (source and optional type) that start an evaluation.</param>
/// <param name="Interval">Evaluate this often as well; null for event-only detectors.</param>
public sealed record SuggestionTriggers(IReadOnlyList<SuggestionEventTrigger> Events, TimeSpan? Interval = null)
{
    public bool Matches(MarshalEvent @event) =>
        Events.Any(trigger => string.Equals(trigger.Source, @event.Source, StringComparison.OrdinalIgnoreCase)
            && (trigger.Type is null || string.Equals(trigger.Type, @event.Type, StringComparison.OrdinalIgnoreCase)));
}

/// <summary>An event that starts an evaluation.</summary>
public sealed record SuggestionEventTrigger(string Source, string? Type = null);

/// <summary>One evaluation.</summary>
/// <param name="Owner">The owner it runs for.</param>
/// <param name="Language">The owner's language (<c>de</c> or <c>en</c>), for proposed action summaries.</param>
/// <param name="TimeZone">The owner's time zone.</param>
/// <param name="Event">The event that started it; null on the interval.</param>
public sealed record SuggestionContext(OwnerId Owner, string Language, TimeZoneInfo TimeZone, MarshalEvent? Event);

/// <summary>Something a detector would suggest.</summary>
/// <param name="DedupeKey">The same key within the detector's cooldown is dropped (e.g. the entity id and the day).</param>
/// <param name="Facts">What the wording model may say: names, values, times. The owner's device data, never third-party text.</param>
/// <param name="Action">A proposed action that runs when the user accepts; null for none.</param>
/// <param name="Snippet">Text shown verbatim below the worded suggestion (e.g. a YAML snippet to paste); never sent to the wording model.</param>
public sealed record SuggestionCandidate(string DedupeKey, IReadOnlyDictionary<string, string> Facts, ProposedAction? Action = null, string? Snippet = null);

/// <summary>A tool call a suggestion offers, frozen until the user accepts or it expires.</summary>
/// <param name="Tool">The tool.</param>
/// <param name="Arguments">Its arguments (a JSON object).</param>
/// <param name="Summary">What it does, for the confirmation and the Accept button.</param>
public sealed record ProposedAction(ToolId Tool, JsonElement Arguments, string Summary);
