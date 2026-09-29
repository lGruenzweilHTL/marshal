using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Notifications;
using Marshal.Abstractions.Tasks;
using Marshal.Abstractions.Tools;
using Marshal.Data;
using Marshal.Data.Notifications;
using Marshal.Tasks.Notifications;
using Marshal.Tasks.Store;
using Marshal.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using static Marshal.Testing.TestExecutionContext;

namespace Marshal.Tasks.Tests.Notifications;

public sealed class NotificationToolsTests : IAsyncLifetime
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 29, 10, 0, 0, TimeSpan.Zero));
    private readonly FakeNotificationSender _sender = new();
    private TestDatabase _database = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _database = await TestDatabase.CreateAsync();
        _database.Accessor.Current = Text(OwnerA);
    }

    public async ValueTask DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task Descriptors_SafeAndInboxOutputUntrusted()
    {
        IReadOnlyList<ITool> tools = await Source().GetToolsAsync(Ct);

        Assert.Equal(["notifications.inbox_consume", "notifications.inbox_list", "notifications.notify"], tools.Select(tool => tool.Descriptor.Id.ToString()).Order(StringComparer.Ordinal));
        Assert.All(tools, tool => Assert.Equal(RiskTier.Safe, tool.Descriptor.DefaultTier));
        Assert.Equal(["notifications.inbox_list"], tools.Where(tool => tool.Descriptor.UntrustedOutput).Select(tool => tool.Descriptor.Id.ToString()));
    }

    [Fact]
    public async Task Notify_SentAsAgentWithChannel()
    {
        Result<ToolOutput> result = await InvokeAsync("notify", new { message = "The washing machine is done.", priority = "normal", deliver = new { channel = "voice", target = "assist_satellite.kitchen" } });

        Assert.True(result.IsSuccess);
        NotificationRequest sent = Assert.Single(_sender.Sent);
        Assert.Equal((OwnerA, "agent", NotificationPriority.Normal, "The washing machine is done.", DeliveryChannel.Voice, "assist_satellite.kitchen"), (sent.Owner, sent.Source, sent.Priority, sent.Text, sent.Channel, sent.Target));
    }

    [Fact]
    public async Task Notify_InATaskRun_SourceIsTheTaskWithItsOverride()
    {
        DateTimeOffset until = _time.GetUtcNow().AddHours(20);
        TaskRecord task = (await Store().CreateAsync(new NewTask { Goal = "Watch the print.", Trigger = TaskTrigger.Now(), OverrideQuietHoursUntil = until }, Ct)).Value;

        await InvokeAsync("notify", new { message = "Layer shift detected." }, Text(OwnerA) with { Source = SessionSource.Task, IsInteractive = false, TaskId = task.Id });

        NotificationRequest sent = Assert.Single(_sender.Sent);
        Assert.Equal(($"task:{task.Id}", (TaskId?)task.Id, (DateTimeOffset?)until, (DeliveryChannel?)null), (sent.Source, sent.TaskId, sent.OverrideQuietHoursUntil, sent.Channel));
    }

    [Fact]
    public async Task Notify_Critical_ValidationError()
    {
        Result<ToolOutput> result = await InvokeAsync("notify", new { message = "Wake up!", priority = "critical" });

        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.Empty(_sender.Sent);
    }

    [Fact]
    public async Task InboxListAndConsume_ConsumedItemsNotListedAgain()
    {
        Guid first = await AddInboxAsync("Newsletter: printer firmware 1.2.", _time.GetUtcNow().AddMinutes(-30));
        Guid second = await AddInboxAsync("Package shipped.", _time.GetUtcNow().AddMinutes(-20));
        Guid third = await AddInboxAsync("Backup done.", _time.GetUtcNow().AddMinutes(-10));
        await AddInboxAsync("Bob's item.", _time.GetUtcNow(), OwnerB);

        using (var listed = JsonDocument.Parse((await InvokeAsync("inbox_list", new { })).Value.Content))
            Assert.Equal([third.ToString(), second.ToString(), first.ToString()], listed.RootElement.EnumerateArray().Select(item => item.GetProperty("id").GetString()));

        Assert.Contains("\"consumed\":2", (await InvokeAsync("inbox_consume", new { ids = new[] { first.ToString(), second.ToString() } })).Value.Content, StringComparison.Ordinal);

        using (var listed = JsonDocument.Parse((await InvokeAsync("inbox_list", new { })).Value.Content))
            Assert.Equal([third.ToString()], listed.RootElement.EnumerateArray().Select(item => item.GetProperty("id").GetString()));
        await using MarshalDbContext db = _database.CreateContext(OwnerA);
        Assert.Equal(_time.GetUtcNow(), (await db.Notifications.SingleAsync(row => row.Id == first, Ct)).ConsumedAt);
    }

    [Fact]
    public async Task InboxConsume_OtherOwnersItems_Untouched()
    {
        Guid bobs = await AddInboxAsync("Bob's item.", _time.GetUtcNow(), OwnerB);

        Assert.Contains("\"consumed\":0", (await InvokeAsync("inbox_consume", new { ids = new[] { bobs.ToString() } })).Value.Content, StringComparison.Ordinal);
    }

    private NotificationToolSource Source() =>
        new(_sender, new EfNotificationInbox(_database.Factory, _time), Store(), new ViennaProfiles());

    private EfTaskStore Store() => new(_database.Factory, _database.Accessor, new ViennaProfiles(), Options.Create(new TaskOptions()), _time);

    private async Task<Result<ToolOutput>> InvokeAsync(string name, object arguments, MarshalExecutionContext? context = null)
    {
        MarshalExecutionContext run = context ?? Text(OwnerA);
        using (_database.Accessor.Use(run))
        {
            ITool tool = (await Source().GetToolsAsync(Ct)).Single(candidate => candidate.Descriptor.Id.Name == name);
            return await tool.InvokeAsync(new ToolInvocation(JsonSerializer.SerializeToElement(arguments), run), Ct);
        }
    }

    private async Task<Guid> AddInboxAsync(string text, DateTimeOffset at, Marshal.Abstractions.Identity.OwnerId? owner = null)
    {
        var id = Guid.CreateVersion7();
        await using MarshalDbContext db = _database.CreateContext(owner ?? OwnerA);
        db.Notifications.Add(new Notification { Id = id, OwnerId = owner ?? OwnerA, Source = "task:1", Priority = NotificationPriority.Low, Content = text, Outcome = NotificationOutcome.Inboxed, CreatedAt = at });
        await db.SaveChangesAsync(Ct);
        return id;
    }
}
