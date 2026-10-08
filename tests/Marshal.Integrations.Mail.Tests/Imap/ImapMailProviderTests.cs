using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using Marshal.Abstractions;
using Marshal.Abstractions.Mail;
using Marshal.Integrations.Mail.Credentials;
using Marshal.Integrations.Mail.Imap;
using Marshal.Testing;
using MimeKit;
using MailFolder = Marshal.Abstractions.Mail.MailFolder;

namespace Marshal.Integrations.Mail.Tests.Imap;

public sealed class ImapMailProviderTests(ImapServers servers)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Search_TranslatesEachCriterion()
    {
        MailAccount account = servers.Dovecot("private");
        await ImapServers.AppendAsync(account, Message("alice@example.com", "Invoice September", "Amount due.", new DateTime(2026, 9, 1)));
        await ImapServers.AppendAsync(account, Message("bob@example.com", "Lunch", "Tomorrow?", new DateTime(2026, 9, 20)), seen: true);
        await ImapServers.AppendAsync(account, Message("alice@example.com", "Re: Lunch", "Pizza is fine.", new DateTime(2026, 9, 21)), seen: true);
        ImapMailProvider provider = Provider();

        Assert.Equal(["Re: Lunch", "Invoice September"], await SubjectsAsync(provider, account, new MailQuery(From: "alice")));
        Assert.Equal(["Invoice September"], await SubjectsAsync(provider, account, new MailQuery(Subject: "invoice")));
        Assert.Equal(["Re: Lunch"], await SubjectsAsync(provider, account, new MailQuery(Text: "pizza")));
        Assert.Equal(["Lunch"], await SubjectsAsync(provider, account, new MailQuery(To: "bob-friend")));
        Assert.Equal(["Invoice September"], await SubjectsAsync(provider, account, new MailQuery(Unread: true)));
        Assert.Equal(["Re: Lunch", "Lunch"], await SubjectsAsync(provider, account, new MailQuery(Since: new DateOnly(2026, 9, 15))));
        Assert.Equal(["Invoice September"], await SubjectsAsync(provider, account, new MailQuery(Before: new DateOnly(2026, 9, 15))));
        Assert.Equal(["Re: Lunch"], await SubjectsAsync(provider, account, new MailQuery(Limit: 1)));
        MailSummary hit = Assert.Single((await provider.SearchAsync(account, new MailQuery(Subject: "invoice"), Ct)).Value);
        Assert.Equal(("alice@example.com", "INBOX", true), (hit.From, hit.Folder, hit.Unread));
    }

    [Fact]
    public async Task ReadMessage_TextBodyHtmlConvertedAndAttachmentsByName()
    {
        MailAccount account = servers.Dovecot("private");
        MimeMessage html = Message("shop@example.com", "Your order", "", new DateTime(2026, 9, 2));
        var body = new BodyBuilder { HtmlBody = "<html><head><style>p{color:red}</style></head><body><p>Order <b>42</b> shipped.</p><p>Thanks</p><script>alert(1)</script></body></html>" };
        body.Attachments.Add("invoice.pdf", "%PDF-1.4"u8.ToArray(), new ContentType("application", "pdf"));
        html.Body = body.ToMessageBody();
        await ImapServers.AppendAsync(account, html);
        ImapMailProvider provider = Provider();
        string id = Assert.Single((await provider.SearchAsync(account, new MailQuery(), Ct)).Value).Id;

        MailMessage message = (await provider.ReadMessageAsync(account, id, Ct)).Value;

        Assert.Equal("Your order", message.Subject);
        Assert.Contains("Order 42 shipped.", message.Body, StringComparison.Ordinal);
        Assert.Contains("Thanks", message.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("<", message.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("alert", message.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("color", message.Body, StringComparison.Ordinal);
        Assert.Equal(["invoice.pdf"], message.Attachments);
    }

    [Fact]
    public async Task ReadAttachment_ByNameOrPositionAndUnknownNamesTheAttachments()
    {
        MailAccount account = servers.Dovecot("private");
        MimeMessage mail = Message("shop@example.com", "Your order", "", new DateTime(2026, 9, 2));
        var body = new BodyBuilder { TextBody = "See attached." };
        body.Attachments.Add("invoice.pdf", "%PDF-1.4 invoice"u8.ToArray(), new ContentType("application", "pdf"));
        body.Attachments.Add("Photo.PNG", [0x89, 0x50, 0x4E, 0x47], new ContentType("image", "png"));
        mail.Body = body.ToMessageBody();
        await ImapServers.AppendAsync(account, mail);
        ImapMailProvider provider = Provider();
        string id = Assert.Single((await provider.SearchAsync(account, new MailQuery(), Ct)).Value).Id;

        Result<MailAttachment> byName = await provider.ReadAttachmentAsync(account, id, "invoice.pdf", Ct);
        Result<MailAttachment> byPosition = await provider.ReadAttachmentAsync(account, id, "2", Ct);
        Result<MailAttachment> unknown = await provider.ReadAttachmentAsync(account, id, "contract.pdf", Ct);

        Assert.Equal(("invoice.pdf", "Photo.PNG"), (byName.Value.Name, byPosition.Value.Name));
        Assert.Equal("%PDF-1.4 invoice"u8.ToArray(), byName.Value.Content);
        Assert.Equal([0x89, 0x50, 0x4E, 0x47], byPosition.Value.Content);
        Assert.Equal("mail.attachment_not_found", unknown.Error.Code);
        Assert.Contains("invoice.pdf, Photo.PNG", unknown.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadThread_RebuiltFromReferencesAcrossInboxAndSent()
    {
        MailAccount account = servers.Dovecot("private");
        MimeMessage first = Message("alice@example.com", "Trip", "Shall we go?", new DateTime(2026, 9, 1), messageId: "a@example.com");
        MimeMessage reply = Message(account.Settings.UserName, "Re: Trip", "Yes!", new DateTime(2026, 9, 2), messageId: "b@example.com", references: ["a@example.com"]);
        MimeMessage answer = Message("alice@example.com", "Re: Trip", "Great.", new DateTime(2026, 9, 3), messageId: "c@example.com", references: ["a@example.com", "b@example.com"]);
        await ImapServers.AppendAsync(account, first);
        await ImapServers.AppendAsync(account, reply, folder: "Sent");
        await ImapServers.AppendAsync(account, answer);
        await ImapServers.AppendAsync(account, Message("carol@example.com", "Other", "Unrelated.", new DateTime(2026, 9, 4)));
        ImapMailProvider provider = Provider();
        string answerId = (await provider.SearchAsync(account, new MailQuery(Text: "Great"), Ct)).Value.Single().Id;
        string firstId = (await provider.SearchAsync(account, new MailQuery(Text: "Shall we go"), Ct)).Value.Single().Id;

        IReadOnlyList<MailMessage> fromAnswer = (await provider.ReadThreadAsync(account, answerId, Ct)).Value;
        IReadOnlyList<MailMessage> fromFirst = (await provider.ReadThreadAsync(account, firstId, Ct)).Value;

        Assert.Equal(["Shall we go?", "Yes!", "Great."], fromAnswer.Select(message => message.Body.Trim()));
        Assert.Equal(["INBOX", "Sent", "INBOX"], fromAnswer.Select(message => message.Folder));
        Assert.Equal(fromAnswer.Select(message => message.Id), fromFirst.Select(message => message.Id));
    }

    [Fact]
    public async Task Folders_WithSpecialUse()
    {
        MailAccount account = servers.Dovecot("private");

        IReadOnlyList<MailFolder> folders = (await Provider().ListFoldersAsync(account, Ct)).Value;

        Assert.Contains(new MailFolder("INBOX", null), folders);
        Assert.Contains(new MailFolder("Entwürfe", "\\Drafts"), folders);
        Assert.Contains(new MailFolder("Sent", "\\Sent"), folders);
    }

    [Fact]
    public async Task Draft_AppendedToTheSpecialUseFolderWithTheDraftFlag()
    {
        MailAccount account = servers.Dovecot("private");
        ImapMailProvider provider = Provider();

        string id = (await provider.CreateDraftAsync(account, new MailDraft(["bob@example.com"], "Hello", "Draft body."), Ct)).Value;

        using ImapClient client = await ImapServers.ConnectAsync(account);
        IMailFolder drafts = await client.GetFolderAsync("Entwürfe", Ct);
        await drafts.OpenAsync(FolderAccess.ReadOnly, Ct);
        IMessageSummary stored = Assert.Single(await drafts.FetchAsync(0, -1, MessageSummaryItems.Flags | MessageSummaryItems.Envelope, Ct));
        Assert.True(stored.Flags!.Value.HasFlag(MessageFlags.Draft));
        Assert.Equal("Hello", stored.Envelope!.Subject);
        Assert.Equal("Draft body.", (await provider.ReadMessageAsync(account, id, Ct)).Value.Body.Trim());
    }

    [Fact]
    public async Task Draft_ReplyCarriesThreadHeadersAndUpdateReplacesIt()
    {
        MailAccount account = servers.Dovecot("private");
        await ImapServers.AppendAsync(account, Message("alice@example.com", "Trip", "Shall we go?", new DateTime(2026, 9, 1), messageId: "a@example.com"));
        ImapMailProvider provider = Provider();
        string original = Assert.Single((await provider.SearchAsync(account, new MailQuery(), Ct)).Value).Id;

        string draft = (await provider.CreateDraftAsync(account, new MailDraft(["alice@example.com"], "Re: Trip", "Yes.", ReplyTo: original), Ct)).Value;
        string updated = (await provider.UpdateDraftAsync(account, draft, new MailDraft(["alice@example.com"], "Re: Trip", "Yes, gladly.", ReplyTo: original), Ct)).Value;

        MailMessage stored = (await provider.ReadMessageAsync(account, updated, Ct)).Value;
        Assert.Equal(("a@example.com", "Yes, gladly."), (stored.InReplyTo, stored.Body.Trim()));
        Assert.Equal(["a@example.com"], stored.References);
        Assert.Equal("mail.message_not_found", (await provider.ReadMessageAsync(account, draft, Ct)).Error.Code);
        using ImapClient client = await ImapServers.ConnectAsync(account);
        IMailFolder drafts = await client.GetFolderAsync("Entwürfe", Ct);
        await drafts.OpenAsync(FolderAccess.ReadOnly, Ct);
        Assert.Single(await drafts.SearchAsync(SearchQuery.All, Ct));
    }

    [Fact]
    public async Task Draft_UpdateOfANonDraft_Refused()
    {
        MailAccount account = servers.Dovecot("private");
        await ImapServers.AppendAsync(account, Message("alice@example.com", "Trip", "Shall we go?", new DateTime(2026, 9, 1)));
        ImapMailProvider provider = Provider();
        string inboxMessage = Assert.Single((await provider.SearchAsync(account, new MailQuery(), Ct)).Value).Id;

        Result<string> result = await provider.UpdateDraftAsync(account, inboxMessage, new MailDraft(["x@example.com"], "x", "x"), Ct);

        Assert.Equal("mail.not_a_draft", result.Error.Code);
        Assert.Single((await provider.SearchAsync(account, new MailQuery(), Ct)).Value);
    }

    [Fact]
    public async Task Draft_NoSpecialUseFolder_ErrorUnlessConfigured()
    {
        MailAccount unconfigured = servers.GreenMail("private");
        MailAccount configured = servers.GreenMail("work", draftsFolder: "Drafts");
        using (ImapClient client = await ImapServers.ConnectAsync(configured))
            await ImapServers.GetOrCreateAsync(client, "Drafts");
        ImapMailProvider provider = Provider();

        Result<string> missing = await provider.CreateDraftAsync(unconfigured, new MailDraft(["bob@example.com"], "Hi", "Body"), Ct);
        Result<string> stored = await provider.CreateDraftAsync(configured, new MailDraft(["bob@example.com"], "Hi", "Body"), Ct);

        Assert.Equal("mail.drafts_folder_missing", missing.Error.Code);
        Assert.Equal("Hi", (await provider.ReadMessageAsync(configured, stored.Value, Ct)).Value.Subject);
    }

    [Fact]
    public async Task UnknownOrForgedId_NotFound()
    {
        MailAccount account = servers.Dovecot("private");
        ImapMailProvider provider = Provider();

        Assert.Equal("mail.message_not_found", (await provider.ReadMessageAsync(account, "garbage", Ct)).Error.Code);
        Assert.Equal("mail.message_not_found", (await provider.ReadMessageAsync(account, ImapMessageIds.Format("INBOX", 1, 999), Ct)).Error.Code);
    }

    [Fact]
    public async Task WrongPassword_AuthFailed()
    {
        MailAccount account = servers.Dovecot("private");
        var secrets = new FakeSecretStore();
        secrets.Values["mail_password"] = "wrong";

        Result<IReadOnlyList<MailFolder>> result = await new ImapMailProvider([new PasswordCredentialSource(secrets)]).ListFoldersAsync(account, Ct);

        Assert.Equal("mail.auth_failed", result.Error.Code);
    }

    [Fact]
    public async Task OAuthAccount_AuthenticatesWithXOAuth2()
    {
        await using var server = FakeXOAuth2Server.Start();
        MailAccount account = new(
            MailAccountId.New(), TestExecutionContext.OwnerA, "gmail", MailProviderKind.Imap, MailCredentialStrategy.OAuthGoogle, "mail_gmail_refresh",
            new MailAccountSettings("127.0.0.1", server.Port, MailSecurity.None, "lukas@gmail.com"), DateTimeOffset.UnixEpoch);

        await new ImapMailProvider([new FixedCredentials(MailCredentialStrategy.OAuthGoogle, new MailCredential("lukas@gmail.com", "ya29.token", IsAccessToken: true))])
            .ListFoldersAsync(account, Ct);

        string authenticate = Assert.Single(server.Commands, command => command.StartsWith("AUTHENTICATE", StringComparison.Ordinal));
        Assert.StartsWith("AUTHENTICATE XOAUTH2 ", authenticate, StringComparison.Ordinal);
        Assert.Equal("user=lukas@gmail.com\u0001auth=Bearer ya29.token\u0001\u0001", System.Text.Encoding.ASCII.GetString(Convert.FromBase64String(authenticate["AUTHENTICATE XOAUTH2 ".Length..])));
    }

    [Fact]
    public void Capabilities_ImapRebuildsThreadsAndHasNoLabels()
    {
        Assert.Equal(MailCapabilities.None, Provider().Capabilities);
    }

    private static ImapMailProvider Provider()
    {
        var secrets = new FakeSecretStore();
        secrets.Values["mail_password"] = ImapServers.Password;
        return new ImapMailProvider([new PasswordCredentialSource(secrets)]);
    }

    private static async Task<List<string>> SubjectsAsync(ImapMailProvider provider, MailAccount account, MailQuery query) =>
        [.. (await provider.SearchAsync(account, query, Ct)).Value.Select(summary => summary.Subject)];

    private static MimeMessage Message(string from, string subject, string text, DateTime date, string? messageId = null, string[]? references = null)
    {
        var message = new MimeMessage { Subject = subject, Date = new DateTimeOffset(date, TimeSpan.Zero), Body = new TextPart("plain") { Text = text } };
        message.From.Add(MailboxAddress.Parse(from));
        message.To.Add(MailboxAddress.Parse(subject == "Lunch" ? "bob-friend@example.com" : "lukas@example.com"));
        if (messageId is not null)
            message.MessageId = messageId;
        if (references is not null)
        {
            message.InReplyTo = references[^1];
            foreach (string reference in references)
                message.References.Add(reference);
        }

        return message;
    }

    private sealed class FixedCredentials(MailCredentialStrategy strategy, MailCredential credential) : ICredentialSource
    {
        public MailCredentialStrategy Strategy => strategy;

        public Task<Result<MailCredential>> GetAsync(MailAccount account, CancellationToken cancellationToken) => Task.FromResult(Result.Success(credential));
    }
}
