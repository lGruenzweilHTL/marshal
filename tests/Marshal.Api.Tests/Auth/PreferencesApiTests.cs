using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Marshal.Api.Tests.Http;

namespace Marshal.Api.Tests.Auth;

public sealed class PreferencesApiTests : IAsyncLifetime
{
    private const string Preferences = "/api/v1/me/preferences";

    private MarshalApiFactory _factory = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _factory = await AuthFactory.CreateAsync(AuthFactory.Local());

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task Get_NewUser_DefaultsToTheInstanceTimeZone()
    {
        (HttpClient browser, _) = await Browser.SignInAsync(_factory, "user");

        PreferencesBody? preferences = await browser.GetFromJsonAsync<PreferencesBody>(Preferences, Ct);

        Assert.Equal("Europe/Vienna", preferences?.TimeZone);
        Assert.Equal("en", preferences?.Language);
        Assert.False((await browser.GetFromJsonAsync<JsonElement>(Preferences, Ct)).GetProperty("helpMarshalLearn").GetBoolean());
    }

    [Fact]
    public async Task Put_ValidPreferences_AreReturnedByGet()
    {
        (HttpClient browser, _) = await Browser.SignInAsync(_factory, "user");
        var body = new
        {
            language = "de",
            timeZone = "America/New_York",
            quietHours = new[] { new { day = "friday", start = "23:30:00", end = "08:00:00" } },
            notificationTargets = new { person = "person.lukas", push = new[] { "notify.mobile_app_pixel" } },
            dndUntil = "2026-09-28T20:00:00+00:00",
            helpMarshalLearn = true,
        };

        HttpResponseMessage put = await browser.PutAsJsonAsync(Preferences, body, Ct);

        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
        JsonElement stored = await browser.GetFromJsonAsync<JsonElement>(Preferences, Ct);
        Assert.Equal("de", stored.GetProperty("language").GetString());
        Assert.Equal("America/New_York", stored.GetProperty("timeZone").GetString());
        Assert.Equal("friday", stored.GetProperty("quietHours")[0].GetProperty("day").GetString());
        Assert.Equal("person.lukas", stored.GetProperty("notificationTargets").GetProperty("person").GetString());
        Assert.Equal("notify.mobile_app_pixel", stored.GetProperty("notificationTargets").GetProperty("push")[0].GetString());
        Assert.True(stored.GetProperty("helpMarshalLearn").GetBoolean());
    }

    [Fact]
    public async Task Put_UnknownTimeZone_Returns400AndKeepsTheOldValue()
    {
        (HttpClient browser, _) = await Browser.SignInAsync(_factory, "user");

        HttpResponseMessage put = await browser.PutAsJsonAsync(Preferences, Body("en", "Mars/Base"), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
        Assert.Contains("Mars/Base", await put.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.Equal("Europe/Vienna", (await browser.GetFromJsonAsync<PreferencesBody>(Preferences, Ct))?.TimeZone);
    }

    [Fact]
    public async Task Put_LanguageGerman_RunsWithoutRequestLanguageReplyInGerman()
    {
        _factory.Model.ThenText("Hallo!");
        (HttpClient browser, _) = await Browser.SignInAsync(_factory, "user");
        await browser.PutAsJsonAsync(Preferences, Body("de", "Europe/Vienna"), Ct);

        await (await browser.PostAsJsonAsync("/api/v1/conversation/turns", new { text = "hi" }, Ct)).Content.ReadAsStringAsync(Ct);

        Assert.Contains(_factory.Model.Requests[0].Messages, message => message.Text.Contains("Reply in German", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Schema_HasNoBriefingOrInterestFields()
    {
        JsonElement document = await _factory.CreateClient().GetFromJsonAsync<JsonElement>("/openapi/v1.json", Ct);

        JsonElement properties = document.GetProperty("components").GetProperty("schemas").GetProperty("UserPreferencesResponse").GetProperty("properties");
        Assert.Equal(["dndUntil", "helpMarshalLearn", "language", "notificationTargets", "quietHours", "timeZone"], properties.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
    }

    private static object Body(string language, string timeZone) =>
        new { language, timeZone, quietHours = Array.Empty<object>(), notificationTargets = new { person = (string?)null, push = Array.Empty<string>() }, dndUntil = (string?)null };

    private sealed record PreferencesBody(string Language, string TimeZone);
}
