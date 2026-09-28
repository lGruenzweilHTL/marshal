namespace Marshal.Abstractions.Identity;

/// <summary>
/// The owner of a new session and how it was determined (e.g. <c>owner_default</c>).
/// </summary>
public sealed record IdentityResolution(OwnerId OwnerId, string ResolvedBy);
