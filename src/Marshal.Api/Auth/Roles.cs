using Marshal.Abstractions.Tokens;

namespace Marshal.Api.Auth;

/// <summary>
/// Marshal's roles and the scopes they grant to cookie principals.
/// </summary>
internal static class Roles
{
    public const string Admin = "admin";
    public const string User = "user";

    /// <summary><c>user</c> gets every scope except <c>admin</c> and the webhook scopes; <c>admin</c> gets all.</summary>
    public static IEnumerable<string> ScopesOf(IEnumerable<string> roles)
    {
        var granted = new HashSet<string>(StringComparer.Ordinal);
        foreach (string role in roles)
        {
            if (role == Admin)
                granted.UnionWith(ApiScopes.All);
            else if (role == User)
                granted.UnionWith(ApiScopes.All.Where(scope => scope != ApiScopes.Admin && !ApiScopes.IsHook(scope)));
        }

        return granted.Order(StringComparer.Ordinal);
    }
}
