using Marshal.Abstractions;

namespace Marshal.Integrations.CalDav;

/// <summary>
/// The subset of CalDAV (RFC 4791) and WebDAV sync (RFC 6578) Marshal needs: discovery, time-range queries,
/// collection sync, entity tags, GET and conditional PUT. No DELETE: Marshal never deletes events.
/// </summary>
public interface ICalDavClient
{
    /// <summary>Principal, calendar home set, the account's calendar addresses and its event calendars.</summary>
    Task<Result<CalDavDiscovery>> DiscoverAsync(CalDavAccount account, CancellationToken cancellationToken);

    /// <summary>The resources with an event overlapping [<paramref name="start"/>, <paramref name="end"/>) (<c>calendar-query</c>).</summary>
    Task<Result<IReadOnlyList<CalDavResource>>> QueryAsync(CalDavAccount account, Uri calendar, DateTimeOffset start, DateTimeOffset end, CancellationToken cancellationToken);

    /// <summary>Changes since <paramref name="token"/> (all resources without one); fails with <c>caldav.invalid_sync_token</c> when the server no longer knows it.</summary>
    Task<Result<CalDavSyncResult>> SyncAsync(CalDavAccount account, Uri calendar, string? token, CancellationToken cancellationToken);

    /// <summary>The entity tag of every resource in the calendar, for servers without sync support.</summary>
    Task<Result<IReadOnlyDictionary<Uri, string>>> ListETagsAsync(CalDavAccount account, Uri calendar, CancellationToken cancellationToken);

    Task<Result<CalDavResource>> GetAsync(CalDavAccount account, Uri resource, CancellationToken cancellationToken);

    /// <summary>The resource holding the event with <paramref name="uid"/> (and its overrides); null when there is none.</summary>
    Task<Result<CalDavResource?>> FindByUidAsync(CalDavAccount account, Uri calendar, string uid, CancellationToken cancellationToken);

    /// <summary>
    /// Writes a resource: with <paramref name="ifMatch"/> only if it is unchanged, without one only if it does not
    /// exist yet (<c>caldav.conflict</c> otherwise). Returns the new entity tag when the server sends one.
    /// </summary>
    Task<Result<string?>> PutAsync(CalDavAccount account, Uri resource, string calendarData, string? ifMatch, CancellationToken cancellationToken);
}

/// <summary>An account as the client needs it; the password is read from the secret store for every request.</summary>
public sealed record CalDavAccount(string Id, Uri Url, string Username, string PasswordSecret);

/// <summary>What discovery found.</summary>
/// <param name="Principal">The account's principal URL.</param>
/// <param name="Home">The calendar home set.</param>
/// <param name="Addresses">The account's calendar user addresses (e-mail addresses from <c>mailto:</c> entries).</param>
/// <param name="Calendars">Collections that hold events.</param>
public sealed record CalDavDiscovery(Uri Principal, Uri Home, IReadOnlyList<string> Addresses, IReadOnlyList<CalDavCalendar> Calendars);

/// <summary>A calendar collection that can hold events.</summary>
/// <param name="Url">Collection URL.</param>
/// <param name="Name">Last path segment (decoded): the <c>&lt;calendar&gt;</c> part of the calendar id.</param>
/// <param name="DisplayName">Display name, or the name.</param>
/// <param name="CanWrite">Whether the account may write events.</param>
/// <param name="SupportsSync">Whether the collection has a sync token (RFC 6578).</param>
public sealed record CalDavCalendar(Uri Url, string Name, string DisplayName, bool CanWrite, bool SupportsSync);

public sealed record CalDavResource(Uri Href, string? ETag, string Data);

public sealed record CalDavSyncResult(string Token, IReadOnlyList<CalDavChange> Changes);

/// <summary>A changed or removed resource.</summary>
public sealed record CalDavChange(Uri Href, string? ETag, bool Removed);

/// <summary>Expected CalDAV failures.</summary>
public static class CalDavErrors
{
    public static Error AuthFailed(string account) =>
        new("caldav.auth_failed", $"The CalDAV server rejected the credentials of account '{account}'.", ErrorType.Forbidden);

    public static Error NoPassword(string account) =>
        new("caldav.auth_failed", $"The password secret of CalDAV account '{account}' is not set.", ErrorType.Forbidden);

    public static readonly Error InvalidSyncToken = new("caldav.invalid_sync_token", "The server no longer knows the sync token.", ErrorType.Conflict);

    public static readonly Error Conflict = new("caldav.conflict", "The event changed on the server meanwhile.", ErrorType.Conflict);

    public static Error NotFound(Uri resource) => new("caldav.not_found", $"There is no resource {resource.AbsolutePath}.", ErrorType.NotFound);

    public static Error Failed(string message) => new("caldav.failed", message, ErrorType.Unavailable);
}
