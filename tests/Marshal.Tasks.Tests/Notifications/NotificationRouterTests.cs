using System.Collections.Concurrent;
using Marshal.Abstractions;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.LiveEvents;
using Marshal.Abstractions.Notifications;
using Marshal.Abstractions.Tasks;
using Marshal.Data;
using Marshal.Data.Notifications;
using Marshal.Tasks.Notifications;
using Marshal.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using static Marshal.Testing.TestExecutionContext;

namespace Marshal.Tasks.Tests.Notifications;

public sealed class NotificationRouterTests : IAsyncLifetime
{
    private const string Office = "assist_satellite.office";

    // Tuesday 2026-09-29 12:00 in Vienna.
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 29, 10, 0, 0, TimeSpan.Zero));
    private readonly FakeUserPreferencesStore _preferences = new();
    private readonly FakePresence _presence = new();
    private readonly FakeLiveEventPublisher _live = new();
    private readonly RecordingChannel _voice = new(NotificationChannelKind.VoiceAnnounce);
    private readonly RecordingChannel _push = new(NotificationChannelKind.Push);
    private readonly RecordingChannel _conversation = new(NotificationChannelKind.VoiceConversation);
    private readonly NotificationOptions _options = new() { CoalesceWindow = TimeSpan.Zero, PublicUrl = new Uri("https://marshal.example.com/") };
    private TestDatabase _database = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _database = await TestDatabase.CreateAsync();
        _preferences.Stored[OwnerA] = Preferences();
        _presence.Current = new Presence(PresenceState.Home, Office);
    }

    public async ValueTask DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task Low_Inboxed()
    {
        Guid id = await SendAsync(Request("Newsletter digest.") with { Priority = NotificationPriority.Low });

        Assert.Empty(_voice.Messages);
        Assert.Empty(_push.Messages);
        Assert.Equal((NotificationOutcome.Inboxed, (string?)null, "low"), await HistoryAsync(id));
    }

    [Fact]
    public async Task EveryNotification_PublishedToOpenBrowsers()
    {
        Guid held = await SendAsync(Request("Held later.") with { Channel = DeliveryChannel.Inbox });

        LiveEvent live = Assert.Single(_live.Events);
        Assert.Equal(("notifications", "notification.created", OwnerA), (live.Topic, live.Type, live.Owner));
        NotificationPayload payload = Assert.IsType<NotificationPayload>(live.Payload);
        Assert.Equal((held, "Held later.", NotificationOutcome.Inboxed, NotificationPriority.Normal), (payload.Id, payload.Text, payload.Outcome, payload.Priority));
    }

    [Fact]
    public async Task DeliveredLater_PublishedAgainAsUpdated()
    {
        AtLocal(23, 0);
        await SendAsync(Request("The backup finished."));
        _time.SetUtcNow(new DateTimeOffset(2026, 9, 30, 5, 0, 0, TimeSpan.Zero));

        await Router().DeliverDueAsync(Ct);

        Assert.Equal(["notification.created", "notification.updated"], _live.Events.Select(live => live.Type));
        Assert.Equal(NotificationOutcome.Delivered, Assert.IsType<NotificationPayload>(_live.Events.Last().Payload).Outcome);
    }

    [Fact]
    public async Task LowOnPush_SilentPush()
    {
        await SendAsync(Request("Package shipped.") with { Priority = NotificationPriority.Low, Channel = DeliveryChannel.Push });

        Assert.True(Assert.Single(_push.Messages).Silent);
    }

    [Fact]
    public async Task Critical_FromAgent_ValidationError()
    {
        Result<Guid> result = await Router().SendAsync(Request("Wake up!") with { Priority = NotificationPriority.Critical }, Ct);

        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.Empty(_push.Messages);
    }

    [Fact]
    public async Task Critical_AtNightWithDnd_VoiceAndPush()
    {
        AtLocal(3, 0);
        _preferences.Stored[OwnerA] = Preferences() with { DndUntil = _time.GetUtcNow().AddHours(2) };

        Guid id = await SendAsync(Request("Print failure detected; printer paused.") with { Priority = NotificationPriority.Critical, Source = "system:babysitter" });

        Assert.Equal(Office, Assert.Single(_voice.Messages).Satellite);
        ChannelMessage push = Assert.Single(_push.Messages);
        Assert.Equal((NotificationPriority.Critical, false), (push.Priority, push.Silent));
        Assert.Equal((NotificationOutcome.Delivered, "voice+push", "critical"), await HistoryAsync(id));
    }

    [Fact]
    public async Task WhereIAm_Away_Push()
    {
        _presence.Current = new Presence(PresenceState.Away);

        Guid id = await SendAsync(Request("Your print is done."));

        ChannelMessage push = Assert.Single(_push.Messages);
        Assert.Equal(("Your print is done.", false, "https://marshal.example.com/tasks/7"), (push.Text, push.Silent, push.Link));
        Assert.Equal((NotificationOutcome.Delivered, "push", "away"), await HistoryAsync(id));
    }

    [Fact]
    public async Task WhereIAm_HomeInOffice_VoiceOnOfficeSatellite()
    {
        Guid id = await SendAsync(Request("Your print is done."));

        Assert.Equal(Office, Assert.Single(_voice.Messages).Satellite);
        Assert.Equal((NotificationOutcome.Delivered, "voice", "presence"), await HistoryAsync(id));
    }

    [Fact]
    public async Task WhereIAm_HomeNoOccupiedRoom_Push()
    {
        _presence.Current = new Presence(PresenceState.Home);

        Guid id = await SendAsync(Request("Your print is done."));

        Assert.Single(_push.Messages);
        Assert.Equal((NotificationOutcome.Delivered, "push", "no_room"), await HistoryAsync(id));
    }

    [Fact]
    public async Task VoiceTarget_SpokenThere()
    {
        await SendAsync(Request("Dinner is ready.") with { Channel = DeliveryChannel.Voice, Target = "assist_satellite.kitchen" });

        Assert.Equal("assist_satellite.kitchen", Assert.Single(_voice.Messages).Satellite);
    }

    [Fact]
    public async Task QuietHours_HeldUntilTheyEnd()
    {
        AtLocal(23, 0);

        Guid id = await SendAsync(Request("The backup finished."));

        Assert.Empty(_voice.Messages);
        Assert.Equal((NotificationOutcome.Held, (string?)null, "quiet_hours"), await HistoryAsync(id));
        _time.SetUtcNow(new DateTimeOffset(2026, 9, 30, 4, 59, 0, TimeSpan.Zero)); // 06:59
        await Router().DeliverDueAsync(Ct);
        Assert.Empty(_voice.Messages);

        _time.SetUtcNow(new DateTimeOffset(2026, 9, 30, 5, 0, 0, TimeSpan.Zero)); // 07:00
        await Router().DeliverDueAsync(Ct);

        Assert.Single(_voice.Messages);
        Assert.Equal((NotificationOutcome.Delivered, "voice", "presence"), await HistoryAsync(id));
    }

    [Fact]
    public async Task QuietHours_SilentPushMode()
    {
        _options.QuietHoursMode = QuietHoursMode.SilentPush;
        AtLocal(23, 0);

        Guid id = await SendAsync(Request("The backup finished."));

        Assert.True(Assert.Single(_push.Messages).Silent);
        Assert.Equal((NotificationOutcome.Delivered, "push", "quiet_hours"), await HistoryAsync(id));
    }

    [Fact]
    public async Task QuietHours_Override_Voice()
    {
        AtLocal(3, 0);

        await SendAsync(Request("The print failed.") with { OverrideQuietHoursUntil = _time.GetUtcNow().AddHours(5) });

        Assert.Single(_voice.Messages);
    }

    [Fact]
    public async Task Dnd_VoiceBecomesPush()
    {
        _preferences.Stored[OwnerA] = Preferences() with { DndUntil = _time.GetUtcNow().AddHours(2) };

        Guid id = await SendAsync(Request("Your print is done."));

        Assert.Empty(_voice.Messages);
        Assert.Equal((NotificationOutcome.Delivered, "push", "dnd"), await HistoryAsync(id));
    }

    [Fact]
    public async Task Coalescing_ThreeWithinAMinute_OneMessage()
    {
        _options.CoalesceWindow = TimeSpan.FromMinutes(2);

        Guid first = await SendAsync(Request("Mail from Bob."));
        _time.Advance(TimeSpan.FromSeconds(20));
        Guid second = await SendAsync(Request("Mail from Carol.") with { Source = "task:8" });
        _time.Advance(TimeSpan.FromSeconds(30));
        Guid third = await SendAsync(Request("Mail from Dave.") with { Source = "task:9" });
        Assert.Empty(_voice.Messages);

        _time.Advance(TimeSpan.FromMinutes(2));
        await Router().DeliverDueAsync(Ct);

        Assert.Equal("Mail from Bob. Mail from Carol. Mail from Dave.", Assert.Single(_voice.Messages).Text);
        Assert.Equal((NotificationOutcome.Delivered, "voice", "presence"), await HistoryAsync(first));
        Assert.Equal((NotificationOutcome.Coalesced, (string?)null, "coalesced"), await HistoryAsync(second));
        Assert.Equal((NotificationOutcome.Coalesced, (string?)null, "coalesced"), await HistoryAsync(third));
    }

    [Fact]
    public async Task Coalescing_ActionsAndFollowUpNeverWait()
    {
        _options.CoalesceWindow = TimeSpan.FromMinutes(2);

        await SendAsync(Request("Approve the reminder?") with { Channel = DeliveryChannel.Push, Actions = [new NotificationAction("MARSHAL_APPROVE_n1", "Approve")] });
        await SendAsync(Request("Should I order more filament?") with { Channel = DeliveryChannel.Voice, FollowUp = true });

        Assert.Single(_push.Messages);
        Assert.Single(_conversation.Messages);
    }

    [Fact]
    public async Task FollowUp_ConversationChannelWithPendingAction()
    {
        var action = Abstractions.Guard.PendingActionId.New();

        await SendAsync(Request("Shall I pause the print?") with { Channel = DeliveryChannel.Voice, FollowUp = true, PendingActionId = action });

        ChannelMessage message = Assert.Single(_conversation.Messages);
        Assert.Equal((Office, action), (message.Satellite, message.PendingActionId));
        Assert.Empty(_voice.Messages);
    }

    [Fact]
    public async Task SourceVoiceCap_SecondWithinThirtyMinutesPushed()
    {
        await SendAsync(Request("Layer 30 reached."));
        _time.Advance(TimeSpan.FromMinutes(10));

        Guid second = await SendAsync(Request("Layer 60 reached."));

        Assert.Single(_voice.Messages);
        Assert.Equal((NotificationOutcome.Delivered, "push", "source_voice_cap"), await HistoryAsync(second));
    }

    [Fact]
    public async Task GlobalVoiceCap_SeventhInAnHourPushed()
    {
        for (int i = 0; i < 6; i++)
        {
            await SendAsync(Request($"Update {i}.") with { Source = $"task:{i}" });
            _time.Advance(TimeSpan.FromMinutes(5));
        }

        Guid seventh = await SendAsync(Request("Update 7.") with { Source = "task:70" });

        Assert.Equal(6, _voice.Messages.Count);
        Assert.Equal((NotificationOutcome.Delivered, "push", "voice_cap"), await HistoryAsync(seventh));
    }

    [Fact]
    public async Task VoiceFails_PushInstead_NoPushTarget_Inbox()
    {
        _voice.Fail = true;

        Guid pushed = await SendAsync(Request("Your print is done."));
        Assert.Equal((NotificationOutcome.Delivered, "push", "voice_failed"), await HistoryAsync(pushed));

        _preferences.Stored[OwnerA] = Preferences() with { NotificationTargets = new NotificationTargets("person.alice", []) };
        Guid inboxed = await SendAsync(Request("Your other print is done.") with { Source = "task:8" });
        Assert.Equal((NotificationOutcome.Inboxed, (string?)null, "no_push_target"), await HistoryAsync(inboxed));
    }

    [Fact]
    public async Task Fallback_RecordedAsFallback()
    {
        Guid id = await SendAsync(Request("Good morning.") with { Channel = DeliveryChannel.Push, IsFallback = true });

        Assert.Equal((NotificationOutcome.Fallback, "push", "requested"), await HistoryAsync(id));
    }

    private NotificationRouter Router() => new(
        _database.Factory,
        _database.Accessor,
        _preferences,
        _presence,
        [_voice, _push, _conversation],
        _live,
        Options.Create(_options),
        _time);

    private async Task<Guid> SendAsync(NotificationRequest request)
    {
        Result<Guid> sent = await Router().SendAsync(request, Ct);
        Assert.True(sent.IsSuccess, sent.IsFailure ? sent.Error.Message : null);
        return sent.Value;
    }

    private static NotificationRequest Request(string text) => new()
    {
        Owner = OwnerA,
        Source = "task:7",
        Priority = NotificationPriority.Normal,
        Text = text,
        Link = "tasks/7",
    };

    private static UserPreferencesRecord Preferences() => new(
        "en",
        "Europe/Vienna",
        [new QuietHoursWindow(DayOfWeek.Tuesday, new TimeOnly(22, 0), new TimeOnly(7, 0))],
        new NotificationTargets("person.alice", ["notify.mobile_app_pixel"]),
        null);

    // Local time in Vienna (UTC+2) on the test day; hours before noon are the next morning.
    private void AtLocal(int hour, int minute) =>
        _time.SetUtcNow(new DateTimeOffset(2026, 9, hour < 12 ? 30 : 29, hour, minute, 0, TimeSpan.FromHours(2)));

    private async Task<(NotificationOutcome Outcome, string? Channel, string? Reason)> HistoryAsync(Guid id)
    {
        await using MarshalDbContext db = _database.CreateContext(OwnerA);
        Notification row = await db.Notifications.SingleAsync(notification => notification.Id == id, Ct);
        return (row.Outcome, row.Channel, row.Reason);
    }

    private sealed class FakePresence : IPresenceProvider
    {
        public Presence Current { get; set; } = Presence.Unknown;

        public Task<Presence> GetAsync(OwnerId owner, CancellationToken cancellationToken) => Task.FromResult(Current);
    }

    private sealed class RecordingChannel(NotificationChannelKind kind) : INotificationChannel
    {
        public ConcurrentQueue<ChannelMessage> Messages { get; } = new();

        public bool Fail { get; set; }

        public NotificationChannelKind Kind => kind;

        public Task<Result> DeliverAsync(ChannelMessage message, CancellationToken cancellationToken)
        {
            if (Fail)
                return Task.FromResult(Result.Failure(new Error("channel.failed", "Satellite offline.", ErrorType.Unavailable)));
            Messages.Enqueue(message);
            return Task.FromResult(Result.Success());
        }
    }
}
