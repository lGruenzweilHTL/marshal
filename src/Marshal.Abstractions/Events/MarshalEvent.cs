using System.Text.Json;
using Marshal.Abstractions.Identity;

namespace Marshal.Abstractions.Events;

/// <summary>
/// Something that happened outside the agent, normalized by its source. Payloads can carry third-party text (mail
/// subjects, HA attribute values), so a run started by an event treats its payload as untrusted data unless the
/// source declares its events trusted in code (<see cref="Trusted"/>).
/// </summary>
/// <param name="Source">Source name, e.g. <c>ha</c>, <c>printer</c>, <c>mail</c>.</param>
/// <param name="Type">Event type within the source, e.g. <c>state_changed</c>, <c>complete</c>.</param>
/// <param name="Entity">What the event is about, e.g. <c>person.alice</c> or a printer name; null when nothing specific.</param>
/// <param name="Payload">Event data (a JSON object).</param>
/// <param name="OwnerId">The owner the event belongs to; null for instance-wide events (HA, printers), which every owner's watches see.</param>
/// <param name="Time">When the source saw it.</param>
/// <param name="Trusted">Whether the source declares the payload free of third-party text; set only by source code, never from content or configuration.</param>
public sealed record MarshalEvent(string Source, string Type, string? Entity, JsonElement Payload, OwnerId? OwnerId, DateTimeOffset Time, bool Trusted = false);
