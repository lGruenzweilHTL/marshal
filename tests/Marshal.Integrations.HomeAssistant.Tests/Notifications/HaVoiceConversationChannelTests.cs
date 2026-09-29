using System.Text.Json.Nodes;
using Marshal.Abstractions;
using Marshal.Abstractions.FollowUps;
using Marshal.Abstractions.Sessions;
using Marshal.Abstractions.Notifications;
using Marshal.Abstractions.Tasks;
using Marshal.Integrations.HomeAssistant.Notifications;
using Microsoft.Extensions.Options;
using static Marshal.Testing.TestExecutionContext;

namespace Marshal.Integrations.HomeAssistant.Tests.Notifications;

public sealed class HaVoiceConversationChannelTests : IAsyncLifetime
{
    private readonly RecordingFollowUps _followUps = new();
    private FakeHomeAssistant _ha = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _ha = await FakeHomeAssistant.StartAsync();

    public async ValueTask DisposeAsync() => await _ha.DisposeAsync();

    [Fact]
    public async Task Deliver_StartsConversationWithFollowUpNonce()
    {
        var task = new TaskId(Guid.CreateVersion7());

        Result result = await Channel().DeliverAsync(new ChannelMessage(Guid.NewGuid(), OwnerA, "Good morning. Want the details?", NotificationPriority.Normal, Satellite: "assist_satellite.kitchen", TaskId: task), Ct);

        Assert.True(result.IsSuccess);
        NewFollowUp created = Assert.Single(_followUps.Created);
        Assert.Equal(new NewFollowUp("assist_satellite.kitchen", task), created);
        (string path, JsonNode? body) = Assert.Single(_ha.RestRequests);
        Assert.Equal("/api/services/assist_satellite/start_conversation", path);
        Assert.Equal(("assist_satellite.kitchen", "Good morning. Want the details?", "marshal-followup:nonce-1"), ((string?)body!["entity_id"], (string?)body["start_message"], (string?)body["extra_system_prompt"]));
    }

    [Fact]
    public async Task Deliver_NoSatellite_FailsWithoutRecord()
    {
        Result result = await Channel().DeliverAsync(new ChannelMessage(Guid.NewGuid(), OwnerA, "Hello?", NotificationPriority.Normal), Ct);

        Assert.True(result.IsFailure);
        Assert.Empty(_followUps.Created);
    }

    private HaVoiceConversationChannel Channel() => new(
        new HomeAssistantClient(new TestServerHttp(_ha.Server), Options.Create(new HomeAssistantOptions { Url = new Uri("http://localhost/"), Token = FakeHomeAssistant.Token })),
        _followUps);

    private sealed class RecordingFollowUps : IFollowUpStore
    {
        public List<NewFollowUp> Created { get; } = [];

        public Task<FollowUpRecord> CreateAsync(NewFollowUp followUp, CancellationToken cancellationToken)
        {
            Created.Add(followUp);
            return Task.FromResult(new FollowUpRecord(Guid.NewGuid(), $"nonce-{Created.Count}", followUp.SatelliteId, followUp.TaskId, followUp.PendingActionId, DateTimeOffset.UnixEpoch, null));
        }

        public Task<FollowUpRecord?> ConsumeAsync(string nonce, string satelliteId, SessionId session, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<FollowUpRecord?> ConsumeRecentAsync(string satelliteId, TimeSpan window, SessionId session, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
