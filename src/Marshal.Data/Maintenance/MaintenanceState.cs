using Marshal.Abstractions.Identity;

namespace Marshal.Data.Maintenance;

/// <summary>
/// A maintenance item's counter baseline (the value when it was last done) and what was already notified. Items are
/// configured per instance and belong to the instance owner.
/// </summary>
public sealed class MaintenanceState : IOwned
{
    public required OwnerId OwnerId { get; init; }

    /// <summary>The item id from <c>maintenance.items</c>.</summary>
    public required string ItemId { get; init; }

    public decimal Baseline { get; set; }

    /// <summary>When the user marked it done; null until then.</summary>
    public DateTimeOffset? LastDoneAt { get; set; }

    /// <summary>The last counter value read, for status while the counter is unavailable.</summary>
    public decimal LastValue { get; set; }

    /// <summary>The counter's unit at the last read.</summary>
    public string? Unit { get; set; }

    public DateTimeOffset LastReadAt { get; set; }

    public bool NotifiedSoon { get; set; }

    public bool NotifiedDue { get; set; }
}
