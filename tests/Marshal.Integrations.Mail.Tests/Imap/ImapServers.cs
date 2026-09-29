using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Security;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Mail;
using MimeKit;

[assembly: AssemblyFixture(typeof(Marshal.Integrations.Mail.Tests.Imap.ImapServers))]

namespace Marshal.Integrations.Mail.Tests.Imap;

/// <summary>
/// A Dovecot (SPECIAL-USE, German drafts folder) and a GreenMail (no SPECIAL-USE) container, shared by all tests.
/// Every test uses its own user, so mailboxes never overlap. Any user logs in with <see cref="Password"/>.
/// </summary>
public sealed class ImapServers : IAsyncLifetime
{
    public const string Password = "secret";

    private const int DovecotImap = 31143;
    private const int GreenMailImap = 3143;

    private readonly IContainer _dovecot = new ContainerBuilder("dovecot/dovecot:2.4.1")
        .WithEnvironment("USER_PASSWORD", Password)
        .WithResourceMapping(new FileInfo(Path.Combine(AppContext.BaseDirectory, "Imap", "dovecot-marshal-test.conf")), "/etc/dovecot/conf.d/")
        .WithPortBinding(DovecotImap, assignRandomHostPort: true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("starting up"))
        .Build();

    private readonly IContainer _greenMail = new ContainerBuilder("greenmail/standalone:2.1.3")
        .WithEnvironment("GREENMAIL_OPTS", "-Dgreenmail.setup.test.imap -Dgreenmail.hostname=0.0.0.0 -Dgreenmail.auth.disabled")
        .WithPortBinding(GreenMailImap, assignRandomHostPort: true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Starting GreenMail"))
        .Build();

    public async ValueTask InitializeAsync()
    {
        await Task.WhenAll(_dovecot.StartAsync(), _greenMail.StartAsync());
        await WaitForImapAsync(Dovecot("probe"));
        await WaitForImapAsync(GreenMail("probe"));
    }

    public async ValueTask DisposeAsync()
    {
        await _dovecot.DisposeAsync();
        await _greenMail.DisposeAsync();
    }

    /// <summary>An account on Dovecot for a fresh user.</summary>
    public MailAccount Dovecot(string alias, string? draftsFolder = null) =>
        Account(alias, _dovecot.Hostname, _dovecot.GetMappedPublicPort(DovecotImap), draftsFolder);

    /// <summary>An account on GreenMail (no SPECIAL-USE) for a fresh user.</summary>
    public MailAccount GreenMail(string alias, string? draftsFolder = null) =>
        Account(alias, _greenMail.Hostname, _greenMail.GetMappedPublicPort(GreenMailImap), draftsFolder);

    /// <summary>A logged-in MailKit client for seeding and checking the account's mailbox.</summary>
    public static async Task<ImapClient> ConnectAsync(MailAccount account)
    {
        var client = new ImapClient();
        await client.ConnectAsync(account.Settings.Host, account.Settings.Port, SecureSocketOptions.None);
        await client.AuthenticateAsync(account.Settings.UserName, Password);
        return client;
    }

    /// <summary>Appends <paramref name="message"/> to <paramref name="folder"/> (created when missing).</summary>
    public static async Task AppendAsync(MailAccount account, MimeMessage message, string folder = "INBOX", bool seen = false)
    {
        using ImapClient client = await ConnectAsync(account);
        IMailFolder target = folder == "INBOX" ? client.Inbox : await GetOrCreateAsync(client, folder);
        await target.AppendAsync(new AppendRequest(message, seen ? MessageFlags.Seen : MessageFlags.None));
        await client.DisconnectAsync(true);
    }

    public static async Task<IMailFolder> GetOrCreateAsync(ImapClient client, string folder)
    {
        try
        {
            return await client.GetFolderAsync(folder) ?? throw new FolderNotFoundException(folder);
        }
        catch (FolderNotFoundException)
        {
            IMailFolder root = client.GetFolder(client.PersonalNamespaces[0]);
            return await root.CreateAsync(folder, isMessageFolder: true) ?? throw new FolderNotFoundException(folder);
        }
    }

    private static MailAccount Account(string alias, string host, int port, string? draftsFolder) => new(
        MailAccountId.New(),
        new OwnerId(Guid.CreateVersion7()),
        alias,
        MailProviderKind.Imap,
        MailCredentialStrategy.Password,
        "mail_password",
        new MailAccountSettings(host, port, MailSecurity.None, $"user{Guid.NewGuid():N}"[..20] + "@example.com", draftsFolder),
        DateTimeOffset.UnixEpoch);

    private static async Task WaitForImapAsync(MailAccount account)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                using ImapClient client = await ConnectAsync(account);
                await client.DisconnectAsync(true);
                return;
            }
            catch (Exception) when (attempt < 50)
            {
                await Task.Delay(200);
            }
        }
    }
}
