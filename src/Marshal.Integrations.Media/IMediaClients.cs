using Marshal.Abstractions;

namespace Marshal.Integrations.Media;

/// <summary>
/// Seerr (<c>/api/v1</c>): search, title details, the request list and creating a request. No other write exists.
/// Failures use <c>media.unavailable</c>; an unknown TMDB id is <c>media.not_found</c>.
/// </summary>
public interface ISeerrClient
{
    /// <summary>Movies and shows matching <paramref name="query"/> (people are dropped), in Seerr's order.</summary>
    Task<Result<IReadOnlyList<SeerrTitle>>> SearchAsync(string query, CancellationToken cancellationToken);

    /// <summary>Details of one title, including seasons for shows.</summary>
    Task<Result<SeerrTitle>> GetTitleAsync(MediaType type, int tmdbId, CancellationToken cancellationToken);

    /// <summary>The newest requests, at most <paramref name="take"/>, without titles (Seerr's list has none).</summary>
    Task<Result<IReadOnlyList<SeerrRequest>>> GetRequestsAsync(int take, CancellationToken cancellationToken);

    /// <summary>Requests a title; <paramref name="seasons"/> null means all seasons of a show.</summary>
    Task<Result> CreateRequestAsync(MediaType type, int tmdbId, IReadOnlyList<int>? seasons, CancellationToken cancellationToken);
}

/// <summary>Sonarr or Radarr (API v3), read only: calendars and download queues. Failures use <c>media.unavailable</c>.</summary>
public interface IArrClient
{
    /// <summary>Whether <paramref name="kind"/> is configured.</summary>
    bool IsConfigured(ArrKind kind);

    /// <summary>Episodes (Sonarr) or movie releases (Radarr) between <paramref name="start"/> and <paramref name="end"/>.</summary>
    Task<Result<IReadOnlyList<ArrCalendarEntry>>> GetCalendarAsync(ArrKind kind, DateTimeOffset start, DateTimeOffset end, CancellationToken cancellationToken);

    /// <summary>The download queue.</summary>
    Task<Result<IReadOnlyList<ArrQueueItem>>> GetQueueAsync(ArrKind kind, CancellationToken cancellationToken);
}

public enum MediaType
{
    Movie,
    Show,
}

public enum ArrKind
{
    Sonarr,
    Radarr,
}

/// <summary>Seerr's media status, as model-facing words.</summary>
public enum MediaAvailability
{
    Unknown,
    Pending,
    Processing,
    PartiallyAvailable,
    Available,
}

/// <summary>Seerr's request status.</summary>
public enum RequestStatus
{
    Pending,
    Approved,
    Declined,
    Failed,
    Completed,
}

/// <summary>A movie or show as Seerr knows it; <paramref name="Seasons"/> lists season numbers without specials (0).</summary>
public sealed record SeerrTitle(
    MediaType Type,
    int TmdbId,
    string Title,
    int? Year,
    string? Overview,
    IReadOnlyList<int>? Seasons,
    MediaAvailability Availability,
    string? JellyfinItemId);

/// <summary>One request from Seerr's list.</summary>
public sealed record SeerrRequest(MediaType Type, int TmdbId, RequestStatus Status, MediaAvailability Availability, DateTimeOffset RequestedAt);

/// <summary>A calendar entry: an episode (season and episode set) or a movie release (<paramref name="Release"/> set).</summary>
public sealed record ArrCalendarEntry(
    ArrKind Kind,
    string Title,
    int? Season,
    int? Episode,
    string? EpisodeTitle,
    string? Release,
    DateTimeOffset Date,
    bool Downloaded);

/// <summary>A queue record; <paramref name="Percent"/> is 0–100.</summary>
public sealed record ArrQueueItem(ArrKind Kind, string Title, int? Season, int? Episode, double Percent, string? TimeLeft, DateTimeOffset? EstimatedCompletion, string State);
