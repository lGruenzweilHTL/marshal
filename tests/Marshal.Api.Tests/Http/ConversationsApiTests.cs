using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Sessions;
using Marshal.Abstractions.Tasks;
using Marshal.Api.Tests.Auth;
using Marshal.Data.Tasks;

namespace Marshal.Api.Tests.Http;

public sealed class ConversationsApiTests : IAsyncLifetime
{
    private const string Conversations = "/api/v1/conversations";
    private const string Turns = "/api/v1/conversation/turns";

    private MarshalApiFactory _factory = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _factory = await AuthFactory.CreateAsync(AuthFactory.Local());

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task List_OwnConversationsNewestFirst_PagedWithBefore()
    {
        _factory.Model.ThenText("one").ThenText("two");
        (HttpClient browser, _) = await Browser.SignInAsync(_factory, "user");
        string first = await TurnAsync(browser, "first question");
        string second = await TurnAsync(browser, "second question");

        List<JsonElement> all = (await browser.GetFromJsonAsync<List<JsonElement>>(Conversations, Ct))!;
        List<JsonElement> page = (await browser.GetFromJsonAsync<List<JsonElement>>($"{Conversations}?limit=1", Ct))!;
        string before = Uri.EscapeDataString(page[0].GetProperty("lastTurnAt").GetString()!);
        List<JsonElement> next = (await browser.GetFromJsonAsync<List<JsonElement>>($"{Conversations}?limit=1&before={before}", Ct))!;

        Assert.Equal([second, first], all.Select(conversation => conversation.GetProperty("id").GetString()));
        Assert.Equal(("web", "second question", true, 1), (all[0].GetProperty("source").GetString(), all[0].GetProperty("title").GetString(), all[0].GetProperty("canContinue").GetBoolean(), all[0].GetProperty("turns").GetInt32()));
        Assert.Equal([second], page.Select(conversation => conversation.GetProperty("id").GetString()));
        Assert.Equal([first], next.Select(conversation => conversation.GetProperty("id").GetString()));
    }

    [Fact]
    public async Task Detail_TurnsAndBackgroundResultWithKinds()
    {
        _factory.Model.ThenText("Working on it.");
        (HttpClient browser, OwnerId user) = await Browser.SignInAsync(_factory, "user");
        string id = await TurnAsync(browser, "check the printer");
        await _factory.WithDatabaseAsync(user, async db =>
        {
            DateTimeOffset now = DateTimeOffset.UtcNow.AddSeconds(1);
            db.AgentTasks.Add(new AgentTask
            {
                Id = TaskId.New(),
                OwnerId = user,
                CreatedBy = TaskCreator.User,
                Kind = TaskKind.Agent,
                Goal = "check the printer",
                TriggerKind = TaskTriggerKind.Now,
                TriggerSpec = "",
                OriginSessionId = new SessionId(Guid.Parse(id)),
                Status = AgentTaskStatus.Done,
                ResultBody = "The printer finished at 14:02.",
                ResultAt = now,
                CreatedAt = now,
                UpdatedAt = now,
            });
            return await db.SaveChangesAsync(Ct);
        });

        JsonElement detail = await browser.GetFromJsonAsync<JsonElement>($"{Conversations}/{id}", Ct);

        Assert.Equal(id, detail.GetProperty("conversation").GetProperty("id").GetString());
        JsonElement[] entries = [.. detail.GetProperty("entries").EnumerateArray()];
        Assert.Equal(["turn", "backgroundResult"], entries.Select(entry => entry.GetProperty("kind").GetString()));
        Assert.Equal(("check the printer", "Working on it.", "text", "ok"), (entries[0].GetProperty("input").GetString(), entries[0].GetProperty("reply").GetString(), entries[0].GetProperty("inputMode").GetString(), entries[0].GetProperty("outcome").GetString()));
        Assert.Equal("The printer finished at 14:02.", entries[1].GetProperty("body").GetString());
    }

    [Fact]
    public async Task Detail_ForeignOrUnknownId_Returns404()
    {
        _factory.Model.ThenText("hello");
        (HttpClient alice, _) = await Browser.SignInAsync(_factory, "user");
        (HttpClient bob, _) = await Browser.SignInAsync(_factory, "user");
        string id = await TurnAsync(alice, "hi");

        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"{Conversations}/{id}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync($"{Conversations}/{Guid.NewGuid()}", Ct)).StatusCode);
        Assert.Empty((await bob.GetFromJsonAsync<List<JsonElement>>(Conversations, Ct))!);
    }

    [Fact]
    public async Task Tokens_CannotReadConversations()
    {
        HttpClient token = await _factory.ClientWithTokenAsync(Abstractions.Tokens.ApiScopes.ConversationWrite);

        Assert.Equal(HttpStatusCode.Forbidden, (await token.GetAsync(Conversations, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await token.GetAsync($"{Conversations}/{Guid.NewGuid()}", Ct)).StatusCode);
    }

    private static async Task<string> TurnAsync(HttpClient browser, string text)
    {
        HttpResponseMessage response = await browser.PostAsJsonAsync(Turns, new { text }, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return Sse.Parse(await response.Content.ReadAsStringAsync(Ct))[^1].Data.GetProperty("conversationId").GetString()!;
    }
}
