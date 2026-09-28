using Marshal.Abstractions.Identity;

namespace Marshal.Data.Identity;

/// <summary>
/// A user of the instance. Becomes the ASP.NET Identity user in auth-web-shell.
/// </summary>
public sealed class User
{
    public required OwnerId Id { get; init; }

    public required string UserName { get; set; }

    public required string DisplayName { get; set; }

    public string? PasswordHash { get; set; }

    public required string SecurityStamp { get; set; }

    public required DateTimeOffset CreatedAt { get; init; }
}
