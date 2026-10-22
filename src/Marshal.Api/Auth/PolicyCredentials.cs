namespace Marshal.Api.Auth;

/// <summary>
/// The credentials an authorization policy accepts, for the OpenAPI document: the browser session and/or a machine token.
/// </summary>
/// <param name="Session">Whether a browser session (cookie) passes.</param>
/// <param name="TokenScopes">Scopes a machine token needs; empty for any token, <see langword="null"/> when no token passes.</param>
internal sealed record PolicyCredentials(bool Session, IReadOnlyList<string>? TokenScopes)
{
    /// <summary>
    /// The credentials that pass both policies, as when an endpoint requires both: a session only if both accept one,
    /// a token only if both accept one, with the scopes of both.
    /// </summary>
    public PolicyCredentials Combine(PolicyCredentials other) => new(
        Session && other.Session,
        TokenScopes is null || other.TokenScopes is null ? null : [.. TokenScopes.Union(other.TokenScopes, StringComparer.Ordinal)]);
}
