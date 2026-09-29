using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Marshal.Abstractions.Identity;
using Microsoft.AspNetCore.WebUtilities;

namespace Marshal.Api.Mail;

/// <summary>
/// Pending OAuth connect flows: the random <c>state</c> sent to the provider maps to the user, provider, alias and
/// PKCE verifier. A state is single use and expires after <see cref="Lifetime"/>; the callback only continues when
/// the signed-in user and the provider match the ones that started it.
/// </summary>
internal sealed class MailOAuthFlows(TimeProvider time)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<string, MailOAuthFlow> _pending = new(StringComparer.Ordinal);

    /// <summary>Starts a flow and returns its state and PKCE code challenge (S256).</summary>
    public (string State, string CodeChallenge) Start(OwnerId owner, string provider, string alias, string email)
    {
        DateTimeOffset now = time.GetUtcNow();
        foreach ((string key, MailOAuthFlow flow) in _pending)
        {
            if (flow.ExpiresAt <= now)
                _pending.TryRemove(key, out _);
        }

        string state = RandomToken();
        string verifier = RandomToken();
        _pending[state] = new MailOAuthFlow(owner, provider, alias, email, verifier, now + Lifetime);
        return (state, WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))));
    }

    /// <summary>The flow, once: the state is removed whether it matches or not.</summary>
    public MailOAuthFlow? Take(OwnerId owner, string provider, string? state)
    {
        if (state is null || !_pending.TryRemove(state, out MailOAuthFlow? flow))
            return null;

        return flow.Owner == owner && flow.Provider == provider && flow.ExpiresAt > time.GetUtcNow() ? flow : null;
    }

    // 32 random bytes: 43 base64url characters, the PKCE verifier's minimum length.
    private static string RandomToken() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
}

/// <summary>A pending connect flow.</summary>
internal sealed record MailOAuthFlow(OwnerId Owner, string Provider, string Alias, string Email, string CodeVerifier, DateTimeOffset ExpiresAt);
