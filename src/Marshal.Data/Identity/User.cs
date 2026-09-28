using Marshal.Abstractions.Identity;
using Microsoft.AspNetCore.Identity;

namespace Marshal.Data.Identity;

/// <summary>
/// A human user: the ASP.NET Core Identity user, keyed by <see cref="OwnerId"/> so every user-owned row can reference it.
/// </summary>
public sealed class User : IdentityUser<OwnerId>
{
    /// <summary>Name the system prompt and the UI use for the user.</summary>
    public required string DisplayName { get; set; }

    public required DateTimeOffset CreatedAt { get; init; }
}
