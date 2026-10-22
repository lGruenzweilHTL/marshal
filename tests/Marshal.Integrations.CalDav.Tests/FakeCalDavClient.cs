using System.Text.RegularExpressions;
using Marshal.Abstractions;

namespace Marshal.Integrations.CalDav.Tests;

/// <summary>
/// An in-memory CalDAV account store: discovery per account id, resources per URL with entity tags, conditional
/// writes like a server. Queries return every resource of the calendar; the provider filters by time itself.
/// </summary>
internal sealed partial class FakeCalDavClient : ICalDavClient
{
    private int _version;

    public Dictionary<string, CalDavDiscovery> Discoveries { get; } = [];

    /// <summary>Accounts whose every call fails with this error.</summary>
    public Dictionary<string, Error> Failures { get; } = [];

    public Dictionary<Uri, (string ETag, string Data)> Resources { get; } = [];

    /// <summary>Sync tokens the server no longer knows.</summary>
    public HashSet<string> ExpiredTokens { get; } = [];

    /// <summary>Calendars without sync support: <see cref="SyncAsync"/> is not expected for them.</summary>
    public HashSet<Uri> WithoutSync { get; } = [];

    public List<(Uri Resource, string Data, string? IfMatch)> Puts { get; } = [];

    public List<(Uri Calendar, string? Token)> Syncs { get; } = [];

    public int DiscoverCount { get; private set; }

    public FakeCalDavClient WithAccount(string account, string baseUrl, IReadOnlyList<string> addresses, params (string Name, bool CanWrite)[] calendars)
    {
        Discoveries[account] = new CalDavDiscovery(new Uri(baseUrl + "principal/"), new Uri(baseUrl), addresses,
            [.. calendars.Select(calendar => new CalDavCalendar(new Uri(new Uri(baseUrl), Uri.EscapeDataString(calendar.Name) + "/"), calendar.Name, calendar.Name.ToUpperInvariant(), calendar.CanWrite, true))]);
        return this;
    }

    public Uri Add(Uri calendar, string file, string data)
    {
        var href = new Uri(calendar, file);
        Resources[href] = (NextETag(), data);
        return href;
    }

    public void Remove(Uri href) => Resources.Remove(href);

    public void Change(Uri href, string data) => Resources[href] = (NextETag(), data);

    public Task<Result<CalDavDiscovery>> DiscoverAsync(CalDavAccount account, CancellationToken cancellationToken)
    {
        DiscoverCount++;
        if (Failures.TryGetValue(account.Id, out Error? failure))
            return Task.FromResult<Result<CalDavDiscovery>>(failure);
        return Task.FromResult<Result<CalDavDiscovery>>(Discoveries[account.Id]);
    }

    public Task<Result<IReadOnlyList<CalDavResource>>> QueryAsync(CalDavAccount account, Uri calendar, DateTimeOffset start, DateTimeOffset end, CancellationToken cancellationToken)
    {
        if (Failures.TryGetValue(account.Id, out Error? failure))
            return Task.FromResult<Result<IReadOnlyList<CalDavResource>>>(failure);
        return Task.FromResult<Result<IReadOnlyList<CalDavResource>>>(In(calendar).Select(entry => new CalDavResource(entry.Key, entry.Value.ETag, entry.Value.Data)).ToList());
    }

    public Task<Result<CalDavSyncResult>> SyncAsync(CalDavAccount account, Uri calendar, string? token, CancellationToken cancellationToken)
    {
        Syncs.Add((calendar, token));
        if (Failures.TryGetValue(account.Id, out Error? failure))
            return Task.FromResult<Result<CalDavSyncResult>>(failure);
        if (token is not null && ExpiredTokens.Contains(token))
            return Task.FromResult<Result<CalDavSyncResult>>(CalDavErrors.InvalidSyncToken);

        // The token is the entity tag list; a real server reports only what changed since.
        var now = In(calendar).ToDictionary(entry => entry.Key, entry => entry.Value.ETag);
        Dictionary<Uri, string> before = token is null ? [] : Parse(token);
        List<CalDavChange> changes = [];
        changes.AddRange(now.Where(entry => !before.TryGetValue(entry.Key, out string? old) || old != entry.Value).Select(entry => new CalDavChange(entry.Key, entry.Value, false)));
        changes.AddRange(before.Keys.Where(href => !now.ContainsKey(href)).Select(href => new CalDavChange(href, null, true)));
        return Task.FromResult<Result<CalDavSyncResult>>(new CalDavSyncResult(string.Join('|', now.Select(entry => $"{entry.Key}={entry.Value}")), changes));
    }

    public Task<Result<IReadOnlyDictionary<Uri, string>>> ListETagsAsync(CalDavAccount account, Uri calendar, CancellationToken cancellationToken)
    {
        if (Failures.TryGetValue(account.Id, out Error? failure))
            return Task.FromResult<Result<IReadOnlyDictionary<Uri, string>>>(failure);
        return Task.FromResult<Result<IReadOnlyDictionary<Uri, string>>>(In(calendar).ToDictionary(entry => entry.Key, entry => entry.Value.ETag));
    }

    public Task<Result<CalDavResource>> GetAsync(CalDavAccount account, Uri resource, CancellationToken cancellationToken) =>
        Task.FromResult<Result<CalDavResource>>(Resources.TryGetValue(resource, out (string ETag, string Data) found)
            ? new CalDavResource(resource, found.ETag, found.Data)
            : CalDavErrors.NotFound(resource));

    public Task<Result<CalDavResource?>> FindByUidAsync(CalDavAccount account, Uri calendar, string uid, CancellationToken cancellationToken) =>
        Task.FromResult<Result<CalDavResource?>>(In(calendar)
            .Where(entry => UidLine().Matches(entry.Value.Data).Any(match => match.Groups[1].Value.Trim() == uid))
            .Select(entry => new CalDavResource(entry.Key, entry.Value.ETag, entry.Value.Data))
            .FirstOrDefault());

    public Task<Result<string?>> PutAsync(CalDavAccount account, Uri resource, string calendarData, string? ifMatch, CancellationToken cancellationToken)
    {
        Puts.Add((resource, calendarData, ifMatch));
        bool exists = Resources.TryGetValue(resource, out (string ETag, string Data) current);
        if (ifMatch is null ? exists : !exists || current.ETag != ifMatch)
            return Task.FromResult<Result<string?>>(CalDavErrors.Conflict);

        string etag = NextETag();
        Resources[resource] = (etag, calendarData);
        return Task.FromResult<Result<string?>>(etag);
    }

    private IEnumerable<KeyValuePair<Uri, (string ETag, string Data)>> In(Uri calendar) =>
        Resources.Where(entry => entry.Key.AbsoluteUri.StartsWith(calendar.AbsoluteUri, StringComparison.Ordinal));

    private static Dictionary<Uri, string> Parse(string token) =>
        token.Length == 0 ? [] : token.Split('|').Select(part => part.Split('=', 2)).ToDictionary(pair => new Uri(pair[0]), pair => pair[1]);

    private string NextETag() => $"\"v{++_version}\"";

    [GeneratedRegex("^UID:(.+)$", RegexOptions.Multiline)]
    private static partial Regex UidLine();
}
