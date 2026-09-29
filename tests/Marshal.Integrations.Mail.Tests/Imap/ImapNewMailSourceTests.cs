using Marshal.Abstractions;
using Marshal.Abstractions.Events;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Mail;
using Marshal.Integrations.Mail.Credentials;
using Marshal.Integrations.Mail.Imap;
using Marshal.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using MimeKit;

namespace Marshal.Integrations.Mail.Tests.Imap;

public sealed class ImapNewMailSourceTests(ImapServers servers) : IAsyncDisposable
{
    private readonly FakeEventPublisher _published = new();
    private ImapNewMailSource? _source;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync()
    {
        if (_source is not null)
        {
            await _source.StopAsync(CancellationToken.None);
            _source.Dispose();
        }
    }

    [Fact]
    public async Task Idle_NewMailPublishedExistingMailNot()
    {
        MailAccount account = servers.Dovecot("private");
        await ImapServers.AppendAsync(account, Message("old@example.com", "Already there"));
        await StartAsync(account, new MailWatchTimings { UseIdle = true });

        await ImapServers.AppendAsync(account, Message("alice@example.com", "Dinner tonight?"));

        MarshalEvent published = await NextAsync();
        Assert.Equal(("mail", "new_mail", "private", (OwnerId?)account.Owner), (published.Source, published.Type, published.Entity, published.OwnerId));
        Assert.Equal("Dinner tonight?", published.Payload.GetProperty("subject").GetString());
        Assert.Equal("alice@example.com", published.Payload.GetProperty("from").GetString());
        Assert.Equal("private", published.Payload.GetProperty("account").GetString());
        Assert.True(ImapMessageIds.TryParse(published.Payload.GetProperty("id").GetString()!, out string folder, out _, out _));
        Assert.Equal("INBOX", folder);
        await Task.Delay(300, Ct);
        Assert.Empty(_published.Events);
    }

    [Fact]
    public async Task Polling_NewMailPublished()
    {
        MailAccount account = servers.GreenMail("work");
        await StartAsync(account, new MailWatchTimings { UseIdle = false, PollInterval = TimeSpan.FromMilliseconds(200) });

        await ImapServers.AppendAsync(account, Message("bob@example.com", "Build failed"));
        await ImapServers.AppendAsync(account, Message("bob@example.com", "Build fixed"));

        Assert.Equal("Build failed", (await NextAsync()).Payload.GetProperty("subject").GetString());
        Assert.Equal("Build fixed", (await NextAsync()).Payload.GetProperty("subject").GetString());
    }

    [Fact]
    public async Task NewMail_NotMarkedSeen()
    {
        MailAccount account = servers.Dovecot("private");
        await StartAsync(account, new MailWatchTimings { UseIdle = true });

        await ImapServers.AppendAsync(account, Message("alice@example.com", "Unread stays unread"));
        await NextAsync();

        var secrets = new FakeSecretStore();
        secrets.Values["mail_password"] = ImapServers.Password;
        MailSummary summary = Assert.Single((await new ImapMailProvider([new PasswordCredentialSource(secrets)]).SearchAsync(account, new MailQuery(), Ct)).Value);
        Assert.True(summary.Unread);
    }

    private async Task StartAsync(MailAccount account, MailWatchTimings timings)
    {
        var secrets = new FakeSecretStore();
        secrets.Values["mail_password"] = ImapServers.Password;
        _source = new ImapNewMailSource(
            new Accounts(account),
            new Accounts(account),
            new FakeExecutionContextAccessor(),
            [new PasswordCredentialSource(secrets)],
            _published,
            timings,
            TimeProvider.System,
            NullLogger<ImapNewMailSource>.Instance);
        await _source.StartAsync(Ct);
        await EventuallyAsync(() => _source.IsConnected);
    }

    private async Task<MarshalEvent> NextAsync()
    {
        await EventuallyAsync(() => !_published.Events.IsEmpty);
        Assert.True(_published.Events.TryDequeue(out MarshalEvent? published));
        return published;
    }

    private static MimeMessage Message(string from, string subject)
    {
        var message = new MimeMessage { Subject = subject, Date = DateTimeOffset.UtcNow, Body = new TextPart("plain") { Text = "Hello." } };
        message.From.Add(MailboxAddress.Parse(from));
        message.To.Add(MailboxAddress.Parse("lukas@example.com"));
        return message;
    }

    private static async Task EventuallyAsync(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(20);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Condition not met in time.");
            await Task.Delay(20, Ct);
        }
    }

    /// <summary>One owner with one account.</summary>
    private sealed class Accounts(MailAccount account) : IUserDirectory, IMailAccountStore
    {
        public Task<IReadOnlyList<OwnerId>> ListAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<OwnerId>>([account.Owner]);

        Task<IReadOnlyList<MailAccount>> IMailAccountStore.ListAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<MailAccount>>([account]);

        public Task<MailAccount?> GetAsync(MailAccountId id, CancellationToken cancellationToken) => Task.FromResult<MailAccount?>(account);

        public Task<MailAccount?> FindByAliasAsync(string alias, CancellationToken cancellationToken) => Task.FromResult<MailAccount?>(account);

        public Task<Result<MailAccount>> CreateAsync(NewMailAccount newAccount, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Result> DeleteAsync(MailAccountId id, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
