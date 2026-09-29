using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Mail;
using Marshal.Abstractions.Tools;
using Marshal.Integrations.Mail.Tools;
using Marshal.Testing;

namespace Marshal.Integrations.Mail.Tests.Tools;

public sealed class MailToolsTests
{
    private readonly FakeMailAccountStore _accounts = new();
    private readonly FakeMailProvider _provider = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Tools_OnlyReadingAndDrafting()
    {
        IReadOnlyList<ITool> tools = await ToolsAsync();

        Assert.Equal(
            ["mail.create_draft", "mail.list_folders", "mail.list_labels", "mail.read_message", "mail.read_thread", "mail.search", "mail.update_draft"],
            tools.Select(tool => tool.Descriptor.Id.ToString()).Order());
        Assert.All(tools, tool => Assert.Equal(RiskTier.Safe, tool.Descriptor.DefaultTier));
        Assert.DoesNotContain(tools, tool => tool.Descriptor.Id.Name.Contains("send", StringComparison.Ordinal)
            || tool.Descriptor.Id.Name.Contains("delete", StringComparison.Ordinal)
            || tool.Descriptor.Id.Name.Contains("move", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ContentTools_UntrustedMailAndSensitive()
    {
        IReadOnlyList<ITool> tools = await ToolsAsync();

        foreach (string name in new[] { "search", "read_message", "read_thread" })
        {
            ToolDescriptor descriptor = tools.Single(tool => tool.Descriptor.Id.Name == name).Descriptor;
            Assert.True(descriptor.UntrustedOutput && descriptor.Sensitive, name);
            Assert.Equal("mail", descriptor.TaintKind);
        }
    }

    [Fact]
    public async Task DraftTools_NotEscalatedByTaint()
    {
        IReadOnlyList<ITool> tools = await ToolsAsync();

        Assert.All(tools.Where(tool => tool.Descriptor.Id.Name.EndsWith("_draft", StringComparison.Ordinal)), tool =>
            Assert.False(tool.Descriptor.Persists || tool.Descriptor.UntrustedOutput));
    }

    [Fact]
    public async Task Search_WithoutAccount_AllAccountsLabeledWithAlias()
    {
        _accounts.Add("private");
        _accounts.Add("work");
        _provider.Add("private", "p1", "Dinner", "See you.");
        _provider.Add("work", "w1", "Report", "Attached.");

        ToolOutput output = (await InvokeAsync("search", new { })).Value;

        using var hits = JsonDocument.Parse(output.Content);
        Assert.Equal(
            [("private", "private:p1", "Dinner"), ("work", "work:w1", "Report")],
            hits.RootElement.EnumerateArray().Select(hit => (hit.GetProperty("account").GetString(), hit.GetProperty("id").GetString(), hit.GetProperty("subject").GetString())).OrderBy(hit => hit.Item1));
        Assert.Equal(["private", "work"], _provider.Searches.Select(search => search.Alias).Order());
        Assert.Equal("private:p1,work:w1", string.Join(',', output.Reference!.Split(',').Order()));
    }

    [Fact]
    public async Task Search_ByAliasAndCriteria()
    {
        _accounts.Add("private");
        _accounts.Add("work");

        await InvokeAsync("search", new { account = "Work", subject = "report", since = "2026-09-01", unread = true, limit = 5 });

        (string alias, MailQuery query) = Assert.Single(_provider.Searches);
        Assert.Equal("work", alias);
        Assert.Equal(new MailQuery(Subject: "report", Since: new DateOnly(2026, 9, 1), Unread: true, Limit: 5), query);
    }

    [Fact]
    public async Task UnknownAlias_ListsTheAccounts()
    {
        _accounts.Add("private");

        Error error = (await InvokeAsync("search", new { account = "school" })).Error;

        Assert.Equal("mail.account_not_found", error.Code);
        Assert.Contains("private", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadMessage_ContentWithTheMessageReference()
    {
        _accounts.Add("private");
        _provider.Add("private", "p1", "Dinner", "See you at 8.");

        ToolOutput output = (await InvokeAsync("read_message", new { id = "private:p1" })).Value;

        Assert.Contains("See you at 8.", output.Content, StringComparison.Ordinal);
        Assert.Contains("plan.pdf", output.Content, StringComparison.Ordinal);
        Assert.Equal("private:p1", output.Reference);
    }

    [Fact]
    public async Task ReadThread_ReferencesEveryMessage()
    {
        _accounts.Add("private");
        _provider.Add("private", "p1", "Trip", "Shall we?");
        _provider.Add("private", "p2", "Re: Trip", "Yes.");

        ToolOutput output = (await InvokeAsync("read_thread", new { id = "private:p2" })).Value;

        Assert.Contains("Shall we?", output.Content, StringComparison.Ordinal);
        Assert.Equal("private:p1,private:p2", output.Reference);
    }

    [Fact]
    public async Task ReadMessage_MalformedId_NotFound()
    {
        _accounts.Add("private");

        Assert.Equal("mail.message_not_found", (await InvokeAsync("read_message", new { id = "p1" })).Error.Code);
    }

    [Fact]
    public async Task CreateDraft_ReplyingInTheMessagesAccount()
    {
        _accounts.Add("private");
        _accounts.Add("work");

        ToolOutput output = (await InvokeAsync("create_draft", new { account = "work", to = new[] { "bob@example.com" }, subject = "Hi", body = "Hello Bob", replyTo = "work:w1" })).Value;

        (string alias, MailDraft draft, _) = Assert.Single(_provider.Drafts);
        Assert.Equal("work", alias);
        Assert.Equal(("Hi", "Hello Bob", "w1"), (draft.Subject, draft.Body, draft.ReplyTo));
        Assert.Equal(["bob@example.com"], draft.To);
        Assert.Contains("work:draft-1", output.Content, StringComparison.Ordinal);
        Assert.Contains("not sent", output.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateDraft_ReplyFromAnotherAccount_Refused()
    {
        _accounts.Add("private");
        _accounts.Add("work");

        Error error = (await InvokeAsync("create_draft", new { account = "private", to = new[] { "bob@example.com" }, subject = "Hi", body = "x", replyTo = "work:w1" })).Error;

        Assert.Equal("mail.invalid_arguments", error.Code);
        Assert.Empty(_provider.Drafts);
    }

    [Fact]
    public async Task UpdateDraft_ReplacesInTheDraftsAccount()
    {
        _accounts.Add("private");

        await InvokeAsync("update_draft", new { id = "private:draft-7", to = new[] { "bob@example.com" }, subject = "Hi", body = "New text" });

        (string alias, MailDraft draft, string? replaced) = Assert.Single(_provider.Drafts);
        Assert.Equal(("private", "draft-7", "New text"), (alias, replaced, draft.Body));
    }

    [Fact]
    public async Task Visibility_HiddenWithoutAccountsAndLabelsOnlyWithTheCapability()
    {
        IReadOnlyList<ITool> tools = await ToolsAsync();
        Assert.False(await tools.Single(tool => tool.Descriptor.Id.Name == "search").IsAvailableAsync(TestExecutionContext.Text(), Ct));

        _accounts.Add("private");
        Assert.True(await tools.Single(tool => tool.Descriptor.Id.Name == "search").IsAvailableAsync(TestExecutionContext.Text(), Ct));
        Assert.False(await tools.Single(tool => tool.Descriptor.Id.Name == "list_labels").IsAvailableAsync(TestExecutionContext.Text(), Ct));

        IReadOnlyList<ITool> withLabels = await new MailToolSource(_accounts, [new FakeMailProvider(MailCapabilities.Labels)]).GetToolsAsync(Ct);
        Assert.True(await withLabels.Single(tool => tool.Descriptor.Id.Name == "list_labels").IsAvailableAsync(TestExecutionContext.Text(), Ct));
    }

    [Fact]
    public async Task ListFolders_PerAccount()
    {
        _accounts.Add("private");

        ToolOutput output = (await InvokeAsync("list_folders", new { account = "private" })).Value;

        Assert.Contains("Entwürfe", output.Content, StringComparison.Ordinal);
    }

    private Task<IReadOnlyList<ITool>> ToolsAsync() => new MailToolSource(_accounts, [_provider]).GetToolsAsync(Ct);

    private async Task<Result<ToolOutput>> InvokeAsync(string name, object arguments)
    {
        ITool tool = (await ToolsAsync()).Single(tool => tool.Descriptor.Id.Name == name);
        return await tool.InvokeAsync(new ToolInvocation(JsonSerializer.SerializeToElement(arguments), TestExecutionContext.Text()), Ct);
    }
}
