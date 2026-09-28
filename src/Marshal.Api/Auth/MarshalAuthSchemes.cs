namespace Marshal.Api.Auth;

/// <summary>
/// Authentication scheme names. The default scheme forwards to the token or the cookie scheme per request.
/// </summary>
internal static class MarshalAuthSchemes
{
    /// <summary>Default: bearer requests go to <see cref="ApiToken"/>, everything else to <see cref="Cookie"/>.</summary>
    public const string Default = "Marshal";

    public const string Cookie = "Cookies";

    public const string ApiToken = ApiTokenAuthenticationHandler.SchemeName;
}
