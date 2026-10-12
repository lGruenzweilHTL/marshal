using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Sessions;
using Marshal.Abstractions.Tools;
using Marshal.Api.Tests.Auth;
using Marshal.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Marshal.Api.Tests.Http;

public sealed class WebConversationApiTests : IAsyncLifetime
{
    private const string Turns = "/api/v1/conversation/turns";

    private MarshalApiFactory _factory = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _factory = await AuthFactory.CreateAsync(AuthFactory.Local());

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task Turn_CookieUserWithoutConversationId_StartsWebSessionAndReturnsItsId()
    {
        _factory.Model.ThenText("Hello!");
        (HttpClient browser, _) = await Browser.SignInAsync(_factory, "user");

        JsonElement final = await FinalAsync(await browser.PostAsJsonAsync(Turns, new { text = "hi" }, Ct));

        Assert.Equal("Hello!", final.GetProperty("speech").GetString());
        Assert.True(Guid.TryParse(final.GetProperty("conversationId").GetString(), out _));
    }

    [Fact]
    public async Task Turn_OwnSessionId_ContinuesTheConversation()
    {
        _factory.Model.ThenText("Noted.").ThenText("You said blue.");
        (HttpClient browser, _) = await Browser.SignInAsync(_factory, "user");
        string sessionId = (await FinalAsync(await browser.PostAsJsonAsync(Turns, new { text = "my colour is blue" }, Ct))).GetProperty("conversationId").GetString()!;

        JsonElement second = await FinalAsync(await browser.PostAsJsonAsync(Turns, new { conversationId = sessionId, text = "what did I say?" }, Ct));

        Assert.Equal(sessionId, second.GetProperty("conversationId").GetString());
        Assert.Contains(_factory.Model.Requests[^1].Messages, message => message.Text.Contains("my colour is blue", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Turn_AnotherUsersSessionId_Returns404AndRunsNothing()
    {
        _factory.Model.ThenText("Secret stuff.");
        (HttpClient alice, _) = await Browser.SignInAsync(_factory, "user");
        (HttpClient bob, _) = await Browser.SignInAsync(_factory, "user");
        string aliceSession = (await FinalAsync(await alice.PostAsJsonAsync(Turns, new { text = "hi" }, Ct))).GetProperty("conversationId").GetString()!;

        HttpResponseMessage response = await bob.PostAsJsonAsync(Turns, new { conversationId = aliceSession, text = "what did alice say?" }, Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Single(_factory.Model.Requests);
    }

    [Fact]
    public async Task Turn_PendingActionConfirmedByTyping_ExecutesTheFrozenAction()
    {
        FakeTool unlock = FakeTool.Confirm("test.unlock").Returns("unlocked");
        await using MarshalApiFactory factory = await AuthFactory.CreateAsync(AuthFactory.Local());
        factory.TestServices = services => services.AddSingleton<ITool>(unlock);
        factory.Model.ThenToolCall("test_unlock", new { door = "front" }).ThenText("Should I unlock the front door?").ThenText("Done.");
        (HttpClient browser, _) = await Browser.SignInAsync(factory, "user");

        JsonElement question = await FinalAsync(await browser.PostAsJsonAsync(Turns, new { text = "unlock the front door" }, Ct));
        string sessionId = question.GetProperty("conversationId").GetString()!;
        JsonElement done = await FinalAsync(await browser.PostAsJsonAsync(Turns, new { conversationId = sessionId, text = "confirm" }, Ct));

        Assert.True(question.GetProperty("continueConversation").GetBoolean());
        Assert.Equal("Done.", done.GetProperty("speech").GetString());
        Assert.Single(unlock.Invocations);
    }

    [Fact]
    public async Task Turn_MovedToBackground_ResultJoinsTheSessionHistory()
    {
        _factory.Model
            .ThenToolCall("agent_move_to_background", new { statusMessage = "Working on it." })
            .ThenText("The printer finished at 14:02.")
            .ThenText("About two hours.");
        (HttpClient browser, OwnerId user) = await Browser.SignInAsync(_factory, "user");
        string sessionId = (await FinalAsync(await browser.PostAsJsonAsync(Turns, new { text = "check the printer" }, Ct))).GetProperty("conversationId").GetString()!;
        await WaitForResultAsync(user, sessionId);

        JsonElement second = await FinalAsync(await browser.PostAsJsonAsync(Turns, new { conversationId = sessionId, text = "and how long did it take?" }, Ct));

        Assert.Equal("About two hours.", second.GetProperty("speech").GetString());
        Assert.Contains(_factory.Model.Requests[^1].Messages, message => message.Role == Microsoft.Extensions.AI.ChatRole.Assistant && message.Text == "The printer finished at 14:02.");
    }

    private async Task WaitForResultAsync(OwnerId user, string sessionId)
    {
        var session = new SessionId(Guid.Parse(sessionId));
        for (int attempt = 0; attempt < 200; attempt++)
        {
            bool done = await _factory.WithDatabaseAsync(user, db => db.AgentTasks.AnyAsync(task => task.OriginSessionId == session && task.ResultAt != null, Ct));
            if (done)
                return;

            await Task.Delay(50, Ct);
        }

        Assert.Fail("The background task produced no result.");
    }

    private static async Task<JsonElement> FinalAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        (string name, JsonElement data) = Sse.Parse(await response.Content.ReadAsStringAsync(Ct))[^1];
        Assert.Equal("final", name);
        return data;
    }
}
