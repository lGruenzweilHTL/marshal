namespace Marshal.Api.Auth;

/// <summary>
/// Claim types on Marshal principals.
/// </summary>
internal static class MarshalClaims
{
    public const string Owner = "marshal:owner";
    public const string TokenId = "marshal:token_id";
    public const string Scope = "scope";

    /// <summary>The task an executor callback token is bound to.</summary>
    public const string Task = "marshal:task";

    /// <summary>Name of the login provider a cookie session came from.</summary>
    public const string LoginProvider = "marshal:login_provider";
}
