using Marshal.Abstractions.Identity;

namespace Marshal.Data.Demo;

/// <summary>
/// A row written by the demo seeder, so <c>marshal demo reset</c> removes exactly the seeded rows. Rows that depend on
/// a seeded row (run steps, chunk edges and versions) are removed with it and are not recorded themselves.
/// </summary>
public sealed class DemoSeed : IOwned
{
    public required Guid Id { get; init; }

    public required OwnerId OwnerId { get; init; }

    /// <summary>Table of the seeded row, e.g. <c>runs</c>.</summary>
    public required string Entity { get; init; }

    /// <summary>Primary key of the seeded row.</summary>
    public required string RowId { get; init; }

    public required DateTimeOffset SeededAt { get; init; }
}
