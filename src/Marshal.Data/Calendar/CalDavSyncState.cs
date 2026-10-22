using Marshal.Abstractions.Identity;

namespace Marshal.Data.Calendar;

/// <summary>
/// Where the change poll of one CalDAV calendar stands: the server's sync token (RFC 6578) and the entity tags and
/// event uids of the resources seen, so removals can name their event and servers without sync can be compared.
/// </summary>
public sealed class CalDavSyncState : IOwned
{
    public required OwnerId OwnerId { get; init; }

    /// <summary>Account id from <c>calendar.caldav.accounts</c>.</summary>
    public required string Account { get; init; }

    /// <summary>Calendar name (last path segment of the collection).</summary>
    public required string Calendar { get; init; }

    /// <summary>The last sync token; null for servers without sync support.</summary>
    public string? SyncToken { get; set; }

    /// <summary>JSON object from resource href to <c>{ etag, uid }</c>.</summary>
    public required string EtagMapJson { get; set; }

    public required DateTimeOffset LastPoll { get; set; }
}
