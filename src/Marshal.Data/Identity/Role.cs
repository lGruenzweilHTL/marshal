using Marshal.Abstractions.Identity;
using Microsoft.AspNetCore.Identity;

namespace Marshal.Data.Identity;

/// <summary>
/// An Identity role (<c>admin</c>, <c>user</c>). Roles grant scopes to cookie principals. Identity requires the same
/// key type for users and roles, so role ids are <see cref="OwnerId"/> values too; they never leave the user store.
/// </summary>
public sealed class Role : IdentityRole<OwnerId>;
