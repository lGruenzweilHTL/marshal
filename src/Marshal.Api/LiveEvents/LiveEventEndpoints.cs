using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using Marshal.Abstractions;
using Marshal.Abstractions.LiveEvents;
using Marshal.Api.Auth;
using Marshal.Api.Problems;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Marshal.Api.LiveEvents;

/// <summary>
/// <c>GET /events?topics=a,b</c>: one server-sent event stream per browser tab, multiplexing the requested topics,
/// filtered to the caller's owner. A <c>ping</c> event every 25 seconds keeps proxies from closing it.
/// </summary>
internal static class LiveEventEndpoints
{
    private static readonly TimeSpan _heartbeat = TimeSpan.FromSeconds(25);

    public static RouteGroupBuilder MapLiveEventEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/events", Subscribe)
            .RequireAuthorization()
            .WithTags("Events")
            .WithName("SubscribeLiveEvents")
            .WithSummary("Server-sent live updates for the comma-separated topics (event name = event type, data = LiveEventMessage).");
        return group;
    }

    private static Results<ServerSentEventsResult<LiveEventMessage>, ProblemHttpResult> Subscribe(
        string? topics, ClaimsPrincipal user, ILiveEventSource source, IEnumerable<LiveTopic> known, HttpContext context)
    {
        HashSet<string> requested = [.. (topics ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
        if (requested.Count == 0)
            return new Error("events.topics_required", "Name at least one topic in ?topics=.", ErrorType.Validation).ToProblem();

        HashSet<string> available = [.. known.Select(topic => topic.Name)];
        List<string> unknown = [.. requested.Where(topic => !available.Contains(topic)).Order(StringComparer.Ordinal)];
        if (unknown.Count > 0)
            return new Error("events.unknown_topic", $"Unknown topic(s): {string.Join(", ", unknown)}.", ErrorType.Validation).ToProblem();

        // Subscribe before the response starts so nothing published in between is lost.
        ILiveSubscription subscription = source.Subscribe(user.Owner(), requested);
        context.Response.RegisterForDispose(subscription);
        return TypedResults.ServerSentEvents(ReadAsync(subscription, context.RequestAborted));
    }

    private static async IAsyncEnumerable<SseItem<LiveEventMessage>> ReadAsync(ILiveSubscription subscription, [EnumeratorCancellation] CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            LiveEvent? next;
            try
            {
                next = await subscription.ReadAsync(_heartbeat, ct);
            }
            catch (OperationCanceledException)
            {
                yield break;
            }

            yield return next is null
                ? new SseItem<LiveEventMessage>(LiveEventMessage.Ping, "ping")
                : new SseItem<LiveEventMessage>(new LiveEventMessage(next.Topic, next.Type, next.Id, next.Payload), next.Type) { EventId = next.Id };
        }
    }
}

/// <summary>One live update.</summary>
/// <param name="Topic">Topic, e.g. <c>runs</c>.</param>
/// <param name="Type">Event type, e.g. <c>run.completed</c> (also the SSE event name).</param>
/// <param name="Id">Unique event id.</param>
/// <param name="Payload">Topic-specific data (see <c>web/src/live/events.ts</c>).</param>
public sealed record LiveEventMessage(string Topic, string Type, string Id, object Payload)
{
    public static LiveEventMessage Ping { get; } = new("system", "ping", "", new { });
}
