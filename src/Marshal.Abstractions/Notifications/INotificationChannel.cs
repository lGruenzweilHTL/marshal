using Marshal.Abstractions.Guard;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Tasks;

namespace Marshal.Abstractions.Notifications;

/// <summary>
/// Delivers a routed notification on one channel. The inbox is not a channel: it is the notification record itself.
/// </summary>
public interface INotificationChannel
{
    NotificationChannelKind Kind { get; }

    Task<Result> DeliverAsync(ChannelMessage message, CancellationToken cancellationToken);
}

/// <summary>Delivery channels.</summary>
public enum NotificationChannelKind
{
    /// <summary>Spoken on a satellite.</summary>
    VoiceAnnounce,

    /// <summary>Spoken on a satellite, then listening for a reply.</summary>
    VoiceConversation,

    /// <summary>Companion app push to the person's devices.</summary>
    Push,
}

/// <summary>What a channel delivers.</summary>
/// <param name="NotificationId">The notification record.</param>
/// <param name="Owner">The person to reach.</param>
/// <param name="Text">Text to speak or show.</param>
/// <param name="Priority">Priority (push maps it to the interruption level).</param>
/// <param name="Satellite">Voice: the satellite entity to speak on.</param>
/// <param name="Silent">Push: deliver without sound.</param>
/// <param name="Link">Push: URL the notification opens.</param>
/// <param name="Tag">Push: replaces an earlier push with the same tag.</param>
/// <param name="Actions">Push: action buttons.</param>
/// <param name="TaskId">Task the notification is about (a follow-up reply links to it).</param>
/// <param name="PendingActionId">Pending action a spoken question asks to confirm.</param>
/// <param name="Image">Push: absolute URL of an image to attach.</param>
public sealed record ChannelMessage(
    Guid NotificationId,
    OwnerId Owner,
    string Text,
    NotificationPriority Priority,
    string? Satellite = null,
    bool Silent = false,
    string? Link = null,
    string? Tag = null,
    IReadOnlyList<NotificationAction>? Actions = null,
    TaskId? TaskId = null,
    PendingActionId? PendingActionId = null,
    string? Image = null);
