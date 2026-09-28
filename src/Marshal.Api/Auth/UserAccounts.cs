using System.Security.Claims;
using Marshal.Abstractions.Identity;
using Marshal.Data.Identity;
using Microsoft.AspNetCore.Identity;

namespace Marshal.Api.Auth;

/// <summary>
/// User store operations shared by the login providers: claiming the instance owner, role sync and the session
/// principal.
/// </summary>
internal sealed class UserAccounts(UserManager<User> users, RoleManager<Role> roles, IInstanceOwner instanceOwner)
{
    /// <summary>Serializes "claim the owner or create a user" so two first logins cannot both claim it.</summary>
    public static SemaphoreSlim ProvisioningLock { get; } = new(1, 1);

    public UserManager<User> Users => users;

    /// <summary>
    /// The instance owner seeded on first start while it has no password, passkey or external login; otherwise null.
    /// </summary>
    public async Task<User?> FindUnclaimedOwnerAsync(CancellationToken ct)
    {
        OwnerId ownerId = await instanceOwner.GetAsync(ct);
        User? owner = await users.FindByIdAsync(ownerId.ToString());
        if (owner is null || owner.PasswordHash is not null)
            return null;

        return (await users.GetLoginsAsync(owner)).Count == 0 && (await users.GetPasskeysAsync(owner)).Count == 0 ? owner : null;
    }

    /// <summary>Makes the user's roles exactly <paramref name="wanted"/>.</summary>
    public async Task SyncRolesAsync(User user, IReadOnlyList<string> wanted)
    {
        foreach (string role in wanted)
        {
            if (!await roles.RoleExistsAsync(role))
                Check(await roles.CreateAsync(new Role { Id = OwnerId.New(), Name = role }));
        }

        IList<string> current = await users.GetRolesAsync(user);
        List<string> remove = [.. current.Except(wanted, StringComparer.Ordinal)];
        List<string> add = [.. wanted.Except(current, StringComparer.Ordinal)];
        if (remove.Count > 0)
            Check(await users.RemoveFromRolesAsync(user, remove));
        if (add.Count > 0)
            Check(await users.AddToRolesAsync(user, add));
    }

    /// <summary>The principal stored in the session cookie. Scopes are added per request from the roles.</summary>
    public static ClaimsPrincipal CreatePrincipal(User user, IEnumerable<string> roles, string loginProvider)
    {
        List<Claim> claims =
        [
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(MarshalClaims.Owner, user.Id.ToString()),
            new(ClaimTypes.Name, user.DisplayName),
            new(MarshalClaims.LoginProvider, loginProvider),
            .. roles.Select(role => new Claim(ClaimTypes.Role, role)),
        ];
        return new ClaimsPrincipal(new ClaimsIdentity(claims, MarshalAuthSchemes.Cookie, ClaimTypes.Name, ClaimTypes.Role));
    }

    public static void Check(IdentityResult result)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join(" ", result.Errors.Select(error => error.Description)));
    }
}
