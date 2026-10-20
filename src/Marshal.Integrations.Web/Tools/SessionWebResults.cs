using System.Collections.Concurrent;
using System.Security.Cryptography;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Web;

namespace Marshal.Integrations.Web.Tools;

/// <summary>
/// Search results of a session, addressable by result id for <c>web:&lt;id&gt;</c> references. In memory only, kept
/// for one hour; results are not worth persisting.
/// </summary>
public interface ISessionWebResults
{
    /// <summary>Keeps <paramref name="result"/> for the context's session and returns its result id.</summary>
    string Add(MarshalExecutionContext context, WebResult result);

    /// <summary>The result with <paramref name="id"/> of the context's session, or null when unknown, foreign or expired.</summary>
    WebResult? Find(MarshalExecutionContext context, string id);
}

internal sealed class SessionWebResults(TimeProvider time) : ISessionWebResults
{
    private static readonly TimeSpan _lifetime = TimeSpan.FromHours(1);

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public string Add(MarshalExecutionContext context, WebResult result)
    {
        DateTimeOffset now = time.GetUtcNow();
        foreach ((string key, Entry entry) in _entries)
        {
            if (entry.Expires <= now)
                _entries.TryRemove(key, out _);
        }

        string id = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(6));
        _entries[id] = new Entry(Scope(context), result, now + _lifetime);
        return id;
    }

    public WebResult? Find(MarshalExecutionContext context, string id) =>
        _entries.TryGetValue(id, out Entry? entry) && entry.Scope == Scope(context) && entry.Expires > time.GetUtcNow() ? entry.Result : null;

    // A run without a session (a task run) keeps its results to itself.
    private static string Scope(MarshalExecutionContext context) =>
        $"{context.OwnerId}/{context.SessionId?.ToString() ?? context.RunId?.ToString() ?? "-"}";

    private sealed record Entry(string Scope, WebResult Result, DateTimeOffset Expires);
}
