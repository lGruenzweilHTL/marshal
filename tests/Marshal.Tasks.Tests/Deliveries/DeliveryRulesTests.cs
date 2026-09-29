using Marshal.Abstractions;
using Marshal.Abstractions.Notifications;
using Marshal.Abstractions.Tasks;
using Marshal.Tasks.Deliveries;

namespace Marshal.Tasks.Tests.Deliveries;

public sealed class DeliveryRulesTests
{
    private static readonly TimeZoneInfo _vienna = TimeZoneInfo.FindSystemTimeZoneById("Europe/Vienna");

    private static readonly (TaskDelivery Delivery, string Reason)[] _invalid =
    [
        (new TaskDelivery(DeliveryChannel.Voice, Priority: NotificationPriority.Low), "never use voice"),
        (new TaskDelivery(DeliveryChannel.Push, Priority: NotificationPriority.Critical), "critical"),
        (new TaskDelivery(DeliveryChannel.Inbox, Priority: NotificationPriority.Critical), "critical"),
        (new TaskDelivery(DeliveryChannel.Push, FollowUp: true), "followUp"),
        (new TaskDelivery(DeliveryChannel.Inbox, Target: "assist_satellite.kitchen"), "target"),
        (new TaskDelivery(DeliveryChannel.Voice, When: ["state(area.kitchen = on)"]), "area"),
        (new TaskDelivery(DeliveryChannel.Voice, When: ["kitchen is occupied"]), "not a condition"),
        (new TaskDelivery(DeliveryChannel.Voice, When: ["state(sensor.outside_temperature > warm)"]), "not a number"),
        (new TaskDelivery(DeliveryChannel.Voice, Expires: "11:00"), "fallback"),
        (new TaskDelivery(DeliveryChannel.Voice, Fallback: DeliveryChannel.Push), "needs expires"),
        (new TaskDelivery(DeliveryChannel.Voice, Expires: "tomorrow-ish", Fallback: DeliveryChannel.Push), "deadline"),
        (new TaskDelivery(DeliveryChannel.Push, Priority: NotificationPriority.Low, Expires: "11:00", Fallback: DeliveryChannel.Voice), "never use voice"),
    ];

    public static TheoryData<int> Invalid => [.. Enumerable.Range(0, _invalid.Length)];

    [Theory]
    [MemberData(nameof(Invalid))]
    public void Validate_InvalidDelivery_ValidationError(int index)
    {
        (TaskDelivery delivery, string reason) = _invalid[index];
        Result result = DeliveryRules.Validate([new TaskDelivery(DeliveryChannel.Inbox), delivery]);

        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.Contains(reason, result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_ValidDeliveries_Success()
    {
        Result result = DeliveryRules.Validate(
        [
            new TaskDelivery(DeliveryChannel.Voice, "assist_satellite.kitchen", FollowUp: true),
            new TaskDelivery(DeliveryChannel.Inbox, Priority: NotificationPriority.Low),
            new TaskDelivery(DeliveryChannel.Voice, When: ["state(binary_sensor.kitchen_occupancy = on)", "state($person = home)", "state(zone.home > 0)"], Expires: "11:00", Fallback: DeliveryChannel.Push),
            new TaskDelivery(DeliveryChannel.Silent),
        ]);

        Assert.True(result.IsSuccess);
    }

    [Theory]
    [InlineData("11:00", "2026-09-29T09:00:00Z")] // later today (Vienna is UTC+2 in September)
    [InlineData("06:00", "2026-09-30T04:00:00Z")] // already passed today: tomorrow
    [InlineData("2026-10-26T08:00", "2026-10-26T07:00:00Z")] // after the switch to winter time
    public void ExpiresAt_InOwnersTimeZone(string expires, string expected)
    {
        DateTimeOffset readyAt = new(2026, 9, 29, 6, 5, 0, TimeSpan.Zero); // 08:05 in Vienna

        Assert.Equal(DateTimeOffset.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), DeliveryRules.ExpiresAt(expires, readyAt, _vienna));
    }

    [Theory]
    [InlineData("state(binary_sensor.kitchen_occupancy = on)", "binary_sensor.kitchen_occupancy", ConditionOperator.Equal, "on")]
    [InlineData("state( $person != \"not_home\" )", "$person", ConditionOperator.NotEqual, "not_home")]
    [InlineData("state(zone.home > 0)", "zone.home", ConditionOperator.Greater, "0")]
    [InlineData("state(sensor.outside_temperature < -2.5)", "sensor.outside_temperature", ConditionOperator.Less, "-2.5")]
    public void StateCondition_Parse(string text, string entity, ConditionOperator op, string value)
    {
        Assert.Equal(new StateCondition(entity, op, value), StateCondition.Parse(text).Value);
    }

    [Theory]
    [InlineData("state(binary_sensor.kitchen_occupancy = on)", "on", true)]
    [InlineData("state(binary_sensor.kitchen_occupancy = on)", "ON", true)]
    [InlineData("state(binary_sensor.kitchen_occupancy = on)", "off", false)]
    [InlineData("state(binary_sensor.kitchen_occupancy != on)", "unavailable", false)]
    [InlineData("state(zone.home > 0)", "1", true)]
    [InlineData("state(zone.home > 0)", "0", false)]
    [InlineData("state(zone.home > 0)", "many", false)]
    [InlineData("state(sensor.outside_temperature < -2.5)", "-3", true)]
    public void StateCondition_HoldsFor(string text, string state, bool expected)
    {
        Assert.Equal(expected, StateCondition.Parse(text).Value.HoldsFor(state));
    }
}
