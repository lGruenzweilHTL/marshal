using Marshal.Abstractions.Guard;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Tasks;

namespace Marshal.Abstractions.Notifications;

/// <summary>
/// A notification for one person.
/// </summary>
public sealed record NotificationRequest
{
    /// <summary>Prefix of system-defined sources, the only ones that may send <c>critical</c>.</summary>
    public const string SystemPrefix = "system:";

    public required OwnerId Owner { get; init; }

    /// <summary>Who sends it: <c>task:&lt;id&gt;</c>, <c>agent</c> or <c>system:&lt;name&gt;</c>.</summary>
    public required string Source { get; init; }

    public required NotificationPriority Priority { get; init; }

    /// <summary>Short, speakable text (voice, push text, inbox entry).</summary>
    public required string Text { get; init; }

    /// <summary>Requested channel; null (or voice without a target) means wherever the person is.</summary>
    public DeliveryChannel? Channel { get; init; }

    /// <summary>Voice only: the satellite to speak on.</summary>
    public string? Target { get; init; }

    /// <summary>Voice only: listen for a reply.</summary>
    public bool FollowUp { get; init; }

    public TaskId? TaskId { get; init; }

    /// <summary>Pending action the notification asks to confirm (approvals).</summary>
    public PendingActionId? PendingActionId { get; init; }

    /// <summary>Relative web UI path the push opens, e.g. <c>tasks/&lt;id&gt;</c>.</summary>
    public string? Link { get; init; }

    /// <summary>Push tag (a newer push with the same tag replaces it).</summary>
    public string? Tag { get; init; }

    /// <summary>
    /// Relative path of an image shown with a push (e.g. a shared snapshot link, readable without signing in); sent
    /// only when <c>notifications.publicUrl</c> is set, like the link.
    /// </summary>
    public string? Image { get; init; }

    /// <summary>Push actions (approvals); such notifications are never coalesced.</summary>
    public IReadOnlyList<NotificationAction> Actions { get; init; } = [];

    /// <summary>Quiet hours do not apply until then (set only through a confirmed call).</summary>
    public DateTimeOffset? OverrideQuietHoursUntil { get; init; }

    /// <summary>Sent on a delivery's fallback channel after its conditions expired.</summary>
    public bool IsFallback { get; init; }

    public bool IsSystem => Source.StartsWith(SystemPrefix, StringComparison.Ordinal);
}

/// <summary>A push action button.</summary>
/// <param name="Id">Action identifier returned in <c>mobile_app_notification_action</c>.</param>
/// <param name="Title">Button text.</param>
public sealed record NotificationAction(string Id, string Title);
