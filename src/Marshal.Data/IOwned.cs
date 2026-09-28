using Marshal.Abstractions.Identity;

namespace Marshal.Data;

/// <summary>
/// A user-owned row. Gets an owner-scoped query filter.
/// </summary>
public interface IOwned
{
    /// <summary>The owning user.</summary>
    OwnerId OwnerId { get; }
}
