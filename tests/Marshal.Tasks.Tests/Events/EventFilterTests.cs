using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Events;
using Marshal.Tasks.Events;

namespace Marshal.Tasks.Tests.Events;

public sealed class EventFilterTests
{
    private static readonly DateTimeOffset _at = new(2026, 9, 29, 10, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("""[]""")]
    [InlineData("""{"type":"state_changed"}""")]
    [InlineData("""{"source":""}""")]
    [InlineData("""{"source":"ha","to":{"state":"home"}}""")]
    [InlineData("""not json""")]
    public void Parse_InvalidFilter_ValidationError(string json)
    {
        Result<EventFilter> filter = EventFilter.Parse(json);

        Assert.Equal(ErrorType.Validation, filter.Error.Type);
    }

    [Theory]
    [InlineData("""{"source":"ha"}""", true)]
    [InlineData("""{"source":"HA","type":"state_changed"}""", true)]
    [InlineData("""{"source":"ha","entity":"person.*"}""", true)]
    [InlineData("""{"source":"ha","entity":"person.alice","to":"home"}""", true)]
    [InlineData("""{"source":"ha","entity":"person.alice","to":"not_home"}""", false)]
    [InlineData("""{"source":"ha","entity":"person.bob"}""", false)]
    [InlineData("""{"source":"ha","type":"mobile_app_notification_action"}""", false)]
    [InlineData("""{"source":"printer"}""", false)]
    [InlineData("""{"source":"ha","to":["home","work"]}""", true)]
    [InlineData("""{"source":"ha","attributes.battery":80}""", true)]
    [InlineData("""{"source":"ha","attributes.battery":79}""", false)]
    [InlineData("""{"source":"ha","attributes.charging":true}""", true)]
    [InlineData("""{"source":"ha","attributes.zone":"*"}""", false)]
    [InlineData("""{"source":"ha","from":"not_*"}""", true)]
    public void Matches_SourceTypeEntityAndPayloadConditions(string json, bool expected)
    {
        MarshalEvent @event = new("ha", "state_changed", "person.alice", Payload("""{"from":"not_home","to":"home","attributes":{"battery":80,"charging":true}}"""), null, _at);

        Assert.Equal(expected, EventFilter.Parse(json).Value.Matches(@event));
    }

    [Fact]
    public void Matches_EntityFilterOnEventWithoutEntity_NoMatch()
    {
        MarshalEvent @event = new("mail", "new_mail", null, Payload("{}"), null, _at);

        Assert.False(EventFilter.Parse("""{"source":"mail","entity":"*"}""").Value.Matches(@event));
        Assert.True(EventFilter.Parse("""{"source":"mail","type":"new_*"}""").Value.Matches(@event));
    }

    private static JsonElement Payload(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
