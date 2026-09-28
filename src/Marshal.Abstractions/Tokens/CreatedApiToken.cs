namespace Marshal.Abstractions.Tokens;

/// <summary>
/// A new token. <see cref="Plaintext"/> exists only here; only its hash is stored.
/// </summary>
public sealed record CreatedApiToken(ApiTokenInfo Info, string Plaintext);
