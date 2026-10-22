namespace Marshal.Integrations.CalDav.Sync;

/// <summary>The change poll state of the current owner's CalDAV calendars.</summary>
public interface ICalDavSyncStore
{
    Task<CalDavCalendarSync?> GetAsync(string account, string calendar, CancellationToken cancellationToken);

    Task SaveAsync(CalDavCalendarSync state, CancellationToken cancellationToken);
}

/// <summary>Where the poll of one calendar stands.</summary>
/// <param name="Account">Account id.</param>
/// <param name="Calendar">Calendar name (last path segment of the collection).</param>
/// <param name="Token">The server's sync token; null for servers without sync support.</param>
/// <param name="Resources">Resources seen, by href, with their entity tag and event uid (null until the resource was read).</param>
/// <param name="LastPoll">When the calendar was last polled.</param>
public sealed record CalDavCalendarSync(string Account, string Calendar, string? Token, IReadOnlyDictionary<Uri, CalDavSeenResource> Resources, DateTimeOffset LastPoll);

/// <summary>A resource as last seen.</summary>
public sealed record CalDavSeenResource(string? ETag, string? Uid);
