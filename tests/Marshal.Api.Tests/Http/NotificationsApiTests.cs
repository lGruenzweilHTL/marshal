using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Notifications;
using Marshal.Api.Tests.Auth;
using Marshal.Data.Notifications;

namespace Marshal.Api.Tests.Http;

public sealed class NotificationsApiTests : IAsyncLifetime
{
    private MarshalApiFactory _factory = null!;
    private HttpClient _browser = null!;
    private OwnerId _user;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _factory = new MarshalApiFactory(AuthFactory.Local().ToSettings(0));
        (_browser, _user) = await Browser.SignInAsync(_factory, "user");
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task List_NewestFirstWithContentPriorityChannelOutcomeAndReason()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        await AddAsync(_user, "Washer done.", NotificationOutcome.Delivered, "voice", null, now.AddMinutes(-10));
        await AddAsync(_user, "Parcel arrived.", NotificationOutcome.Held, null, "quiet_hours", now.AddMinutes(-5));
        (_, OwnerId other) = await Browser.SignInAsync(_factory, "user");
        await AddAsync(other, "Not yours.", NotificationOutcome.Delivered, "push", null, now);

        List<JsonElement>? notifications = await _browser.GetFromJsonAsync<List<JsonElement>>("/api/v1/notifications", Ct);

        Assert.NotNull(notifications);
        Assert.Equal(["Parcel arrived.", "Washer done."], notifications.Select(item => item.GetProperty("content").GetString()));
        JsonElement held = notifications[0];
        Assert.Equal(("held", "quiet_hours", "normal", "system:test"),
            (held.GetProperty("outcome").GetString(), held.GetProperty("reason").GetString(), held.GetProperty("priority").GetString(), held.GetProperty("source").GetString()));
        Assert.Equal("voice", notifications[1].GetProperty("channel").GetString());
    }

    [Fact]
    public async Task List_FilteredByOutcome_OnlyHeld()
    {
        await AddAsync(_user, "Washer done.", NotificationOutcome.Delivered, "voice", null, DateTimeOffset.UtcNow);
        await AddAsync(_user, "Parcel arrived.", NotificationOutcome.Held, null, "quiet_hours", DateTimeOffset.UtcNow);

        List<JsonElement>? held = await _browser.GetFromJsonAsync<List<JsonElement>>("/api/v1/notifications?outcome=held", Ct);
        HttpResponseMessage invalid = await _browser.GetAsync("/api/v1/notifications?outcome=shouted", Ct);

        Assert.Equal("Parcel arrived.", Assert.Single(held!).GetProperty("content").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task List_PagesWithBefore()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        await AddAsync(_user, "Old.", NotificationOutcome.Delivered, "push", null, now.AddHours(-2));
        await AddAsync(_user, "New.", NotificationOutcome.Delivered, "push", null, now);

        List<JsonElement>? page = await _browser.GetFromJsonAsync<List<JsonElement>>($"/api/v1/notifications?limit=1&before={Uri.EscapeDataString(now.AddHours(-1).ToString("O"))}", Ct);

        Assert.Equal("Old.", Assert.Single(page!).GetProperty("content").GetString());
    }

    private Task<int> AddAsync(OwnerId owner, string content, NotificationOutcome outcome, string? channel, string? reason, DateTimeOffset createdAt) =>
        _factory.WithDatabaseAsync(owner, db =>
        {
            db.Notifications.Add(new Notification
            {
                Id = Guid.NewGuid(),
                OwnerId = owner,
                Source = "system:test",
                Priority = NotificationPriority.Normal,
                Content = content,
                Channel = channel,
                Outcome = outcome,
                Reason = reason,
                CreatedAt = createdAt,
            });
            return db.SaveChangesAsync(Ct);
        });
}
