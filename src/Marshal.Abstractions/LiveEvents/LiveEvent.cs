using Marshal.Abstractions.Identity;

namespace Marshal.Abstractions.LiveEvents;

/// <summary>
/// A live update for the web UI, delivered only to connections of <paramref name="Owner"/> that asked for
/// <paramref name="Topic"/>.
/// </summary>
/// <param name="Topic">Feature topic, e.g. <c>runs</c>.</param>
/// <param name="Type">Event type within the topic, e.g. <c>run.completed</c>.</param>
/// <param name="Id">Unique event id.</param>
/// <param name="Owner">Owner allowed to see the event.</param>
/// <param name="Payload">Event data (serialized as JSON; never secrets).</param>
public sealed record LiveEvent(string Topic, string Type, string Id, OwnerId Owner, object Payload);

/// <summary>
/// A topic clients can subscribe to. Each feature registers its own.
/// </summary>
/// <param name="Name">Topic name used in <c>?topics=</c>.</param>
public sealed record LiveTopic(string Name);
