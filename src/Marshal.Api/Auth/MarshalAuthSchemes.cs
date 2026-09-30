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

    /// <summary>Executor callback tokens; used only by the callback endpoint's policy, never by the default scheme.</summary>
    public const string ExecutorCallback = ExecutorCallbackAuthenticationHandler.SchemeName;
}
