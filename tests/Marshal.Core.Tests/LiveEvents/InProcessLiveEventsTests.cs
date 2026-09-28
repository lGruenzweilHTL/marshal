using Marshal.Abstractions.LiveEvents;
using Marshal.Core.LiveEvents;
using static Marshal.Testing.TestExecutionContext;

namespace Marshal.Core.Tests.LiveEvents;

public sealed class InProcessLiveEventsTests
{
    private static readonly TimeSpan _wait = TimeSpan.FromSeconds(2);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Subscriber_GetsOnlyItsOwnersEvents()
    {
        var events = new InProcessLiveEvents();
        using ILiveSubscription alice = events.Subscribe(OwnerA, Topics("runs"));

        events.Publish(Event("runs", OwnerB, "bob"));
        events.Publish(Event("runs", OwnerA, "alice"));

        Assert.Equal("alice", (await alice.ReadAsync(_wait, Ct))?.Id);
        Assert.Null(await alice.ReadAsync(TimeSpan.FromMilliseconds(50), Ct));
    }

    [Fact]
    public async Task Subscriber_GetsEveryRequestedTopicOnOneStream()
    {
        var events = new InProcessLiveEvents();
        using ILiveSubscription subscription = events.Subscribe(OwnerA, Topics("runs", "tasks"));

        events.Publish(Event("runs", OwnerA, "1"));
        events.Publish(Event("printer", OwnerA, "2"));
        events.Publish(Event("tasks", OwnerA, "3"));

        Assert.Equal("1", (await subscription.ReadAsync(_wait, Ct))?.Id);
        Assert.Equal("3", (await subscription.ReadAsync(_wait, Ct))?.Id);
    }

    [Fact]
    public async Task Read_NothingPublished_TimesOutWithNull()
    {
        var events = new InProcessLiveEvents();
        using ILiveSubscription subscription = events.Subscribe(OwnerA, Topics("runs"));

        Assert.Null(await subscription.ReadAsync(TimeSpan.FromMilliseconds(20), Ct));
    }

    [Fact]
    public void Dispose_Unsubscribes()
    {
        var events = new InProcessLiveEvents();
        ILiveSubscription subscription = events.Subscribe(OwnerA, Topics("runs"));

        subscription.Dispose();

        Assert.Equal(0, events.SubscriberCount);
    }

    [Fact]
    public async Task SlowSubscriber_LosesOldestEventsInsteadOfBlocking()
    {
        var events = new InProcessLiveEvents(capacity: 2);
        using ILiveSubscription subscription = events.Subscribe(OwnerA, Topics("runs"));

        foreach (string id in new[] { "1", "2", "3" })
            events.Publish(Event("runs", OwnerA, id));

        Assert.Equal("2", (await subscription.ReadAsync(_wait, Ct))?.Id);
        Assert.Equal("3", (await subscription.ReadAsync(_wait, Ct))?.Id);
    }

    private static HashSet<string> Topics(params string[] topics) => [.. topics];

    private static LiveEvent Event(string topic, Abstractions.Identity.OwnerId owner, string id) => new(topic, "test", id, owner, new { });
}
