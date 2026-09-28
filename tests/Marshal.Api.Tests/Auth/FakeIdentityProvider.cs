using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Marshal.Api.Tests.Auth;

/// <summary>
/// An OIDC provider behind an <see cref="HttpMessageHandler"/>: discovery, JWKS and a token endpoint that checks the
/// client secret and the PKCE verifier before issuing a signed ID token.
/// </summary>
internal sealed class FakeIdentityProvider : HttpMessageHandler
{
    public const string Issuer = AuthFactory.Authority;

    private readonly RsaSecurityKey _key = new(RSA.Create(2048)) { KeyId = "test-key" };
    private readonly Dictionary<string, PendingGrant> _grants = [];

    /// <summary>Every token endpoint response body (to prove they never reach the browser).</summary>
    public List<string> IssuedTokens { get; } = [];

    /// <summary>Accepts <paramref name="code"/> for the given authorize request and identity claims.</summary>
    public void Grant(string code, string codeChallenge, string nonce, IDictionary<string, object> claims) =>
        _grants[code] = new PendingGrant(codeChallenge, nonce, claims);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string path = request.RequestUri!.AbsolutePath;
        return path switch
        {
            "/.well-known/openid-configuration" => Json(new Dictionary<string, object>
            {
                ["issuer"] = Issuer,
                ["authorization_endpoint"] = Issuer + "authorize",
                ["token_endpoint"] = Issuer + "token",
                ["jwks_uri"] = Issuer + "jwks",
                ["end_session_endpoint"] = Issuer + "logout",
                ["response_types_supported"] = new[] { "code" },
                ["subject_types_supported"] = new[] { "public" },
                ["id_token_signing_alg_values_supported"] = new[] { "RS256" },
                ["code_challenge_methods_supported"] = new[] { "S256" },
            }),
            "/jwks" => Json(new { keys = new[] { PublicJwk() } }),
            "/token" => await TokenAsync(request, cancellationToken),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        };
    }

    private async Task<HttpResponseMessage> TokenAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var form = (await request.Content!.ReadAsStringAsync(cancellationToken))
            .Split('&')
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(pair => WebUtility.UrlDecode(pair[0]), pair => WebUtility.UrlDecode(pair[1]));
        if (form.GetValueOrDefault("client_secret") != AuthFactory.ClientSecret || !_grants.Remove(form["code"], out PendingGrant? grant))
            return new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = JsonContent.Create(new { error = "invalid_grant" }) };

        string challenge = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(form["code_verifier"])));
        if (challenge != grant.CodeChallenge)
            return new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = JsonContent.Create(new { error = "invalid_grant", error_description = "PKCE" }) };

        var claims = new Dictionary<string, object>(grant.Claims) { ["nonce"] = grant.Nonce };
        string idToken = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = "marshal",
            Claims = claims,
            IssuedAt = DateTime.UtcNow,
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new SigningCredentials(_key, SecurityAlgorithms.RsaSha256),
        });
        string accessToken = "at-" + Guid.NewGuid().ToString("N");
        IssuedTokens.Add(idToken);
        IssuedTokens.Add(accessToken);
        return Json(new { access_token = accessToken, id_token = idToken, token_type = "Bearer", expires_in = 300 });
    }

    private JsonWebKey PublicJwk()
    {
        JsonWebKey jwk = JsonWebKeyConverter.ConvertFromRSASecurityKey(new RsaSecurityKey(_key.Rsa.ExportParameters(false)) { KeyId = _key.KeyId });
        jwk.Use = "sig";
        jwk.Alg = SecurityAlgorithms.RsaSha256;
        return jwk;
    }

    private static HttpResponseMessage Json(object body) => new(HttpStatusCode.OK) { Content = JsonContent.Create(body) };

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _key.Rsa.Dispose();

        base.Dispose(disposing);
    }

    private sealed record PendingGrant(string CodeChallenge, string Nonce, IDictionary<string, object> Claims);
}
