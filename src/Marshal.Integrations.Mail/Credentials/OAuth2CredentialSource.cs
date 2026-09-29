using System.Collections.Concurrent;
using Marshal.Abstractions;
using Marshal.Abstractions.Mail;
using Marshal.Abstractions.Secrets;

namespace Marshal.Integrations.Mail.Credentials;

/// <summary>
/// OAuth 2 access tokens for XOAUTH2, refreshed from the refresh token in the account's owner secret. Access tokens
/// are kept in memory until five minutes before they expire. A rotated refresh token replaces the stored one.
/// </summary>
public sealed class OAuth2CredentialSource(OAuthPreset preset, OAuthTokenClient tokens, ISecretStore secrets, ISecretWriter writer, TimeProvider time) : ICredentialSource, IDisposable
{
    private static readonly TimeSpan _refreshMargin = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<MailAccountId, (string Token, DateTimeOffset ExpiresAt)> _cache = new();
    private readonly SemaphoreSlim _refresh = new(1, 1);

    public MailCredentialStrategy Strategy => preset.Strategy;

    public async Task<Result<MailCredential>> GetAsync(MailAccount account, CancellationToken cancellationToken)
    {
        if (Cached(account) is { } cached)
            return cached;

        await _refresh.WaitAsync(cancellationToken);
        try
        {
            if (Cached(account) is { } refreshedMeanwhile)
                return refreshedMeanwhile;

            Result<SecretName> name = SecretName.Create(account.CredentialSecret);
            Result<string> refreshToken = name.IsSuccess
                ? await secrets.GetForOwnerAsync(name.Value, account.Owner, cancellationToken)
                : name.Error;
            if (refreshToken.IsFailure)
                return MailErrors.CredentialMissing(account.Alias);

            Result<OAuthTokens> refreshed = await tokens.RefreshAsync(preset, refreshToken.Value, cancellationToken);
            if (refreshed.IsFailure)
                return refreshed.Error.Code == "mail.oauth_invalid_grant" ? MailErrors.ReauthorizeRequired(account.Alias) : refreshed.Error;

            if (refreshed.Value.RefreshToken is { } rotated && rotated != refreshToken.Value)
                await writer.SetAsync(name.Value, rotated, account.Owner, cancellationToken);

            _cache[account.Id] = (refreshed.Value.AccessToken, time.GetUtcNow() + refreshed.Value.ExpiresIn);
            return new MailCredential(account.Settings.UserName, refreshed.Value.AccessToken, IsAccessToken: true);
        }
        finally
        {
            _refresh.Release();
        }
    }

    public void Dispose() => _refresh.Dispose();

    private MailCredential? Cached(MailAccount account) =>
        _cache.TryGetValue(account.Id, out (string Token, DateTimeOffset ExpiresAt) entry) && time.GetUtcNow() < entry.ExpiresAt - _refreshMargin
            ? new MailCredential(account.Settings.UserName, entry.Token, IsAccessToken: true)
            : null;
}
