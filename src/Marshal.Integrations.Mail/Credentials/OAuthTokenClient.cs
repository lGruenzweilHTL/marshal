using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Mail;
using Microsoft.Extensions.Options;

namespace Marshal.Integrations.Mail.Credentials;

/// <summary>Tokens from a token endpoint.</summary>
/// <param name="AccessToken">Access token for XOAUTH2.</param>
/// <param name="ExpiresIn">Lifetime of the access token.</param>
/// <param name="RefreshToken">New refresh token, when the provider issued one.</param>
public sealed record OAuthTokens(string AccessToken, TimeSpan ExpiresIn, string? RefreshToken);

/// <summary>
/// The OAuth 2 authorization-code flow with PKCE and refresh-token grants against a <see cref="OAuthPreset"/>.
/// </summary>
public sealed class OAuthTokenClient(IHttpClientFactory httpClients, IOptions<MailOAuthOptions> options)
{
    public const string HttpClientName = "mail-oauth";

    public bool IsConfigured(OAuthPreset preset) => Client(preset)?.IsConfigured == true;

    public Result<Uri> AuthorizationUrl(OAuthPreset preset, Uri redirectUri, string state, string codeChallenge)
    {
        if (Client(preset) is not { IsConfigured: true } client)
            return MailErrors.OAuthNotConfigured(preset.Name);

        var query = new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["client_id"] = client.ClientId,
            ["redirect_uri"] = redirectUri.ToString(),
            ["scope"] = preset.Scope,
            ["state"] = state,
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256",
        };
        foreach ((string key, string value) in preset.AuthorizeParameters)
            query[key] = value;

        return new UriBuilder(preset.AuthorizeEndpoint)
        {
            Query = string.Join('&', query.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}")),
        }.Uri;
    }

    public Task<Result<OAuthTokens>> ExchangeCodeAsync(OAuthPreset preset, string code, Uri redirectUri, string codeVerifier, CancellationToken cancellationToken) =>
        RequestAsync(preset, client => new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri.ToString(),
            ["code_verifier"] = codeVerifier,
            ["client_id"] = client.ClientId,
            ["client_secret"] = client.ClientSecret!,
        }, cancellationToken);

    public Task<Result<OAuthTokens>> RefreshAsync(OAuthPreset preset, string refreshToken, CancellationToken cancellationToken) =>
        RequestAsync(preset, client =>
        {
            var form = new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
                ["client_id"] = client.ClientId,
                ["client_secret"] = client.ClientSecret!,
            };
            if (preset.ScopeOnRefresh)
                form["scope"] = preset.Scope;
            return form;
        }, cancellationToken);

    private async Task<Result<OAuthTokens>> RequestAsync(OAuthPreset preset, Func<OAuthClientOptions, Dictionary<string, string>> form, CancellationToken cancellationToken)
    {
        if (Client(preset) is not { IsConfigured: true } client)
            return MailErrors.OAuthNotConfigured(preset.Name);

        try
        {
            using var content = new FormUrlEncodedContent(form(client));
            using HttpResponseMessage response = await httpClients.CreateClient(HttpClientName).PostAsync(preset.TokenEndpoint, content, cancellationToken);
            using JsonDocument document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            JsonElement root = document.RootElement;
            if (!response.IsSuccessStatusCode || !root.TryGetProperty("access_token", out JsonElement access) || access.GetString() is not { Length: > 0 } token)
            {
                string error = root.TryGetProperty("error", out JsonElement code) ? code.GetString() ?? "" : "";
                return error == "invalid_grant"
                    ? new Error("mail.oauth_invalid_grant", "The authorization was revoked or expired.", ErrorType.Unavailable)
                    : MailErrors.OAuthFailed($"{preset.Name} rejected the token request ({(error.Length > 0 ? error : $"HTTP {(int)response.StatusCode}")}).");
            }

            int seconds = root.TryGetProperty("expires_in", out JsonElement expires) && expires.TryGetInt32(out int value) ? value : 3600;
            string? refresh = root.TryGetProperty("refresh_token", out JsonElement next) ? next.GetString() : null;
            return new OAuthTokens(token, TimeSpan.FromSeconds(seconds), string.IsNullOrEmpty(refresh) ? null : refresh);
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException)
        {
            return MailErrors.OAuthFailed($"{preset.Name} could not be reached: {exception.Message}");
        }
    }

    private OAuthClientOptions? Client(OAuthPreset preset) => preset.Strategy switch
    {
        MailCredentialStrategy.OAuthGoogle => options.Value.Google,
        MailCredentialStrategy.OAuthMicrosoft => options.Value.Microsoft,
        _ => null,
    };
}
