using Marshal.Abstractions.Notifications;

namespace Marshal.Abstractions.Tasks;

/// <summary>
/// One delivery of a task's result, delivered independently of the task's other deliveries.
/// </summary>
/// <param name="Channel">Channel of the delivery.</param>
/// <param name="Target">Voice only: satellite id; null means where the owner is.</param>
/// <param name="Priority"><c>normal</c> or <c>low</c>; never <c>critical</c>.</param>
/// <param name="FollowUp">Voice only: listen for a reply after speaking.</param>
/// <param name="When">State conditions <c>state(&lt;entity_id&gt; &lt;op&gt; &lt;value&gt;)</c> that must all hold.</param>
/// <param name="Expires">Deadline in the owner's time zone (<c>HH:mm</c> or a local ISO 8601 time).</param>
/// <param name="Fallback">Channel used when <paramref name="Expires"/> passes first.</param>
public sealed record TaskDelivery(
    DeliveryChannel Channel,
    string? Target = null,
    NotificationPriority Priority = NotificationPriority.Normal,
    bool FollowUp = false,
    IReadOnlyList<string>? When = null,
    string? Expires = null,
    DeliveryChannel? Fallback = null);
