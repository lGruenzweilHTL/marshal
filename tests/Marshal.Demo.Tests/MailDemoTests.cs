using System.Reflection;
using Marshal.Abstractions;
using Marshal.Abstractions.Mail;
using Marshal.Abstractions.Tools;
using Marshal.Demo.Mail;
using Marshal.Integrations.Mail.Imap;

namespace Marshal.Demo.Tests;

public sealed class MailDemoTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task DemoOff_NoDemoAccount()
    {
        await using var demo = DemoServices.Create(demo: false);

        Assert.Empty(await demo.Get<IMailAccountStore>().ListAsync(Ct));
    }

    [Fact]
    public async Task NoAccounts_DemoAccountListedAndSimulated()
    {
        await using var demo = DemoServices.Create();

        MailAccount account = Assert.Single(await demo.Get<IMailAccountStore>().ListAsync(Ct));
        Assert.Equal(DemoMailAccounts.Alias, account.Alias);
        Assert.Equal(account, await demo.Get<IMailAccountStore>().FindByAliasAsync("demo", Ct));
        Assert.Contains(DemoIntegration.Mail, demo.Get<DemoIntegrations>().SimulatedAtStartup);
    }

    [Fact]
    public async Task RealAccount_DemoAccountHiddenAndRealOneDelegated()
    {
        await using var demo = DemoServices.Create();
        IMailAccountStore store = demo.Get<IMailAccountStore>();

        MailAccount real = (await store.CreateAsync(new NewMailAccount("private", MailProviderKind.Imap, MailCredentialStrategy.Password, "mail_private", new MailAccountSettings("imap.example.org", 993, MailSecurity.Tls, "alex")), Ct)).Value;
        await demo.Get<IMailProvider>().SearchAsync(real, new MailQuery(), Ct);

        Assert.Equal([real], await store.ListAsync(Ct));
        Assert.Single(demo.Mail.Calls);
    }

    [Fact]
    public async Task Tools_SearchReadAndDraftUseTheDemoMailbox()
    {
        await using var demo = DemoServices.Create();
        Dictionary<string, ITool> tools = await demo.ToolsAsync();

        string unread = await DemoServices.InvokeAsync(tools["mail.search"], """{"unread":true}""");
        string message = await DemoServices.InvokeAsync(tools["mail.read_message"], """{"id":"demo:m1"}""");
        await DemoServices.InvokeAsync(tools["mail.create_draft"], """{"to":["jana@example.org"],"subject":"Re: Saturday","body":"Sounds good."}""");
        string drafts = await DemoServices.InvokeAsync(tools["mail.search"], """{"folder":"Drafts"}""");

        Assert.Contains("Invoice 2026-4471", unread);
        Assert.Contains("74.70 EUR", message);
        Assert.Contains("Re: Saturday", drafts);
        Assert.Empty(demo.Mail.Calls);
    }

    [Fact]
    public async Task ReadThread_FindsTheReplyDraft()
    {
        await using var demo = DemoServices.Create();
        IMailProvider provider = demo.Get<IMailProvider>();
        MailAccount account = Assert.Single(await demo.Get<IMailAccountStore>().ListAsync(Ct));

        string draft = (await provider.CreateDraftAsync(account, new MailDraft(["jana@example.org"], "Re: hello", "Hi", ReplyTo: "m3"), Ct)).Value;
        IReadOnlyList<MailMessage> thread = (await provider.ReadThreadAsync(account, "m3", Ct)).Value;

        Assert.Equal(["m3", draft], thread.Select(message => message.Id));
    }

    [Fact]
    public async Task UnknownMessage_NotFound()
    {
        await using var demo = DemoServices.Create();
        MailAccount account = Assert.Single(await demo.Get<IMailAccountStore>().ListAsync(Ct));

        Result<MailMessage> read = await demo.Get<IMailProvider>().ReadMessageAsync(account, "nope", Ct);

        Assert.Equal("mail.message_not_found", read.Error.Code);
    }

    [Fact]
    public async Task DemoAccount_CannotBeDeleted()
    {
        await using var demo = DemoServices.Create();
        IMailAccountStore store = demo.Get<IMailAccountStore>();
        MailAccount account = Assert.Single(await store.ListAsync(Ct));

        Assert.Equal("mail.account_not_found", (await store.DeleteAsync(account.Id, Ct)).Error.Code);
    }

    [Fact]
    public async Task NewMailWatcher_NeverSeesTheDemoAccount()
    {
        await using var demo = DemoServices.Create();

        ImapNewMailSource watcher = demo.Get<ImapNewMailSource>();
        var watched = (IMailAccountStore)typeof(ImapNewMailSource).GetField("<accounts>P", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(watcher)!;

        Assert.Empty(await watched.ListAsync(Ct));
    }

    [Fact]
    public void NoSendMemberExists()
    {
        foreach (Type type in typeof(DemoMailbox).Assembly.GetTypes().Where(type => type.Namespace?.Contains("Mail", StringComparison.Ordinal) == true || type == typeof(DemoMailbox)))
        {
            Assert.DoesNotContain(type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly), member => member.Name.Contains("Send", StringComparison.OrdinalIgnoreCase));
        }
    }
}
