using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using Marshal.Abstractions;
using Marshal.Abstractions.Mail;
using MimeKit;
using MailFolder = Marshal.Abstractions.Mail.MailFolder;

namespace Marshal.Integrations.Mail.Imap;

/// <summary>
/// IMAP over MailKit with one short-lived connection per call. Folders are opened read-only for reading (so reading
/// never marks mail as seen). Drafts are appended to the folder marked <c>\Drafts</c> (RFC 6154), else to the folder
/// named in the account settings, with the <c>\Draft</c> flag. Threads are rebuilt from Message-ID, In-Reply-To and
/// References across the message's folder, the inbox and the sent folder.
/// </summary>
public sealed class ImapMailProvider(IEnumerable<ICredentialSource> credentials) : IMailProvider
{
    private const int MaxFetched = 500;

    private readonly ImapConnector _connector = new(credentials);

    public MailProviderKind Kind => MailProviderKind.Imap;

    public MailCapabilities Capabilities => MailCapabilities.None;

    public Task<Result<IReadOnlyList<MailSummary>>> SearchAsync(MailAccount account, MailQuery query, CancellationToken cancellationToken) =>
        WithClientAsync<IReadOnlyList<MailSummary>>(account, async client =>
        {
            Result<IMailFolder> folder = await FolderAsync(client, query.Folder, cancellationToken);
            if (folder.IsFailure)
                return folder.Error;

            await folder.Value.OpenAsync(FolderAccess.ReadOnly, cancellationToken);
            IList<UniqueId> uids = await folder.Value.SearchAsync(Translate(query), cancellationToken);
            List<UniqueId> newest = [.. uids.OrderByDescending(uid => uid.Id).Take(Math.Min(MaxFetched, Math.Max(1, query.Limit) * 5))];
            if (newest.Count == 0)
                return new List<MailSummary>();

            IList<IMessageSummary> fetched = await folder.Value.FetchAsync(newest, MessageSummaryItems.UniqueId | MessageSummaryItems.Envelope | MessageSummaryItems.Flags | MessageSummaryItems.InternalDate, cancellationToken);
            return fetched
                .Where(summary => summary.Envelope is not null)
                .Select(summary => (Summary: summary, Envelope: summary.Envelope!))
                .Select(pair => (pair.Summary, pair.Envelope, Mail: new MailSummary(
                    ImapMessageIds.Format(folder.Value.FullName, folder.Value.UidValidity, pair.Summary.UniqueId.Id),
                    folder.Value.FullName,
                    Address(pair.Envelope.From),
                    [.. pair.Envelope.To.Mailboxes.Select(mailbox => mailbox.Address)],
                    pair.Envelope.Subject ?? "",
                    pair.Envelope.Date ?? pair.Summary.InternalDate ?? DateTimeOffset.MinValue,
                    pair.Summary.Flags?.HasFlag(MessageFlags.Seen) != true)).Mail)
                .OrderByDescending(summary => summary.Date)
                .Take(Math.Max(1, query.Limit))
                .ToList();
        }, cancellationToken);

    public Task<Result<MailMessage>> ReadMessageAsync(MailAccount account, string messageId, CancellationToken cancellationToken) =>
        WithClientAsync(account, async client =>
        {
            Result<(IMailFolder Folder, UniqueId Uid)> located = await LocateAsync(client, messageId, FolderAccess.ReadOnly, cancellationToken);
            if (located.IsFailure)
                return located.Error;

            (IMailFolder folder, UniqueId uid) = located.Value;
            return await LoadAsync(folder, uid, messageId, cancellationToken);
        }, cancellationToken);

    public Task<Result<IReadOnlyList<MailMessage>>> ReadThreadAsync(MailAccount account, string messageId, CancellationToken cancellationToken) =>
        WithClientAsync<IReadOnlyList<MailMessage>>(account, async client =>
        {
            Result<(IMailFolder Folder, UniqueId Uid)> located = await LocateAsync(client, messageId, FolderAccess.ReadOnly, cancellationToken);
            if (located.IsFailure)
                return located.Error;

            Result<MailMessage> target = await LoadAsync(located.Value.Folder, located.Value.Uid, messageId, cancellationToken);
            if (target.IsFailure)
                return target.Error;
            if (target.Value.MessageId is null)
                return new List<MailMessage> { target.Value };

            HashSet<string> known = [.. target.Value.References, target.Value.MessageId];
            if (target.Value.InReplyTo is { } parent)
                known.Add(parent);
            string root = (target.Value.References.Count > 0 ? target.Value.References[0] : null) ?? target.Value.InReplyTo ?? target.Value.MessageId;
            SearchQuery related = known.Aggregate(
                SearchQuery.HeaderContains("References", root).Or(SearchQuery.HeaderContains("In-Reply-To", target.Value.MessageId)),
                (query, id) => query.Or(SearchQuery.HeaderContains("Message-ID", id)));

            var thread = new Dictionary<string, MailMessage>(StringComparer.Ordinal) { [target.Value.MessageId] = target.Value };
            foreach (IMailFolder folder in await ThreadFoldersAsync(client, located.Value.Folder, cancellationToken))
            {
                await folder.OpenAsync(FolderAccess.ReadOnly, cancellationToken);
                foreach (UniqueId uid in (await folder.SearchAsync(related, cancellationToken)).Take(MaxFetched))
                {
                    Result<MailMessage> message = await LoadAsync(folder, uid, ImapMessageIds.Format(folder.FullName, folder.UidValidity, uid.Id), cancellationToken);
                    if (message.IsSuccess && message.Value.MessageId is { } id && (known.Contains(id) || message.Value.References.Contains(root)))
                        thread.TryAdd(id, message.Value);
                }
            }

            return thread.Values.OrderBy(message => message.Date).ToList();
        }, cancellationToken);

    public Task<Result<IReadOnlyList<MailFolder>>> ListFoldersAsync(MailAccount account, CancellationToken cancellationToken) =>
        WithClientAsync<IReadOnlyList<MailFolder>>(account, async client =>
        {
            if (client.PersonalNamespaces.Count == 0)
                return new List<MailFolder> { new(client.Inbox.FullName, null) };

            IList<IMailFolder> folders = await client.GetFoldersAsync(client.PersonalNamespaces[0], StatusItems.None, subscribedOnly: false, cancellationToken);
            return folders
                .Where(folder => !folder.Attributes.HasFlag(FolderAttributes.NoSelect) && !folder.Attributes.HasFlag(FolderAttributes.NonExistent))
                .Select(folder => new MailFolder(folder.FullName, SpecialUse(folder.Attributes)))
                .ToList();
        }, cancellationToken);

    public Task<Result<IReadOnlyList<string>>> ListLabelsAsync(MailAccount account, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Success<IReadOnlyList<string>>([]));

    public Task<Result<string>> CreateDraftAsync(MailAccount account, MailDraft draft, CancellationToken cancellationToken) =>
        WithClientAsync(account, async client =>
        {
            Result<IMailFolder> drafts = await DraftsFolderAsync(client, account, cancellationToken);
            return drafts.IsFailure ? drafts.Error : await AppendDraftAsync(client, account, drafts.Value, draft, cancellationToken);
        }, cancellationToken);

    public Task<Result<string>> UpdateDraftAsync(MailAccount account, string draftId, MailDraft draft, CancellationToken cancellationToken) =>
        WithClientAsync(account, async client =>
        {
            Result<IMailFolder> drafts = await DraftsFolderAsync(client, account, cancellationToken);
            if (drafts.IsFailure)
                return drafts.Error;

            if (!ImapMessageIds.TryParse(draftId, out string folderName, out uint validity, out uint uid) || folderName != drafts.Value.FullName)
                return NotADraft(draftId);

            await drafts.Value.OpenAsync(FolderAccess.ReadWrite, cancellationToken);
            if (drafts.Value.UidValidity != validity)
                return MailErrors.MessageNotFound(draftId);

            IList<IMessageSummary> existing = await drafts.Value.FetchAsync([new UniqueId(uid)], MessageSummaryItems.Flags, cancellationToken);
            if (existing.Count == 0)
                return MailErrors.MessageNotFound(draftId);
            if (existing[0].Flags?.HasFlag(MessageFlags.Draft) != true)
                return NotADraft(draftId);

            Result<string> replaced = await AppendDraftAsync(client, account, drafts.Value, draft, cancellationToken);
            if (replaced.IsFailure)
                return replaced;

            // Only the superseded draft Marshal wrote is removed; no tool can delete mail.
            await drafts.Value.OpenAsync(FolderAccess.ReadWrite, cancellationToken);
            await drafts.Value.AddFlagsAsync(new UniqueId(uid), MessageFlags.Deleted, silent: true, cancellationToken);
            if (client.Capabilities.HasFlag(ImapCapabilities.UidPlus))
                await drafts.Value.ExpungeAsync([new UniqueId(uid)], cancellationToken);
            else
                await drafts.Value.ExpungeAsync(cancellationToken);
            return replaced;
        }, cancellationToken);

    private static SearchQuery Translate(MailQuery query)
    {
        SearchQuery search = SearchQuery.All;
        if (!string.IsNullOrWhiteSpace(query.From))
            search = search.And(SearchQuery.FromContains(query.From));
        if (!string.IsNullOrWhiteSpace(query.To))
            search = search.And(SearchQuery.ToContains(query.To));
        if (!string.IsNullOrWhiteSpace(query.Subject))
            search = search.And(SearchQuery.SubjectContains(query.Subject));
        if (!string.IsNullOrWhiteSpace(query.Text))
            search = search.And(SearchQuery.MessageContains(query.Text));
        if (query.Since is { } since)
            search = search.And(SearchQuery.SentSince(since.ToDateTime(TimeOnly.MinValue)));
        if (query.Before is { } before)
            search = search.And(SearchQuery.SentBefore(before.ToDateTime(TimeOnly.MinValue)));
        if (query.Unread is { } unread)
            search = search.And(unread ? SearchQuery.NotSeen : SearchQuery.Seen);
        return search;
    }

    private static async Task<Result<IMailFolder>> FolderAsync(ImapClient client, string? name, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name) || string.Equals(name, "INBOX", StringComparison.OrdinalIgnoreCase))
            return Result.Success(client.Inbox);

        try
        {
            return Result.Success(await client.GetFolderAsync(name, cancellationToken));
        }
        catch (FolderNotFoundException)
        {
            return new Error("mail.folder_not_found", $"There is no folder '{name}'.", ErrorType.NotFound);
        }
    }

    private static async Task<Result<(IMailFolder Folder, UniqueId Uid)>> LocateAsync(ImapClient client, string messageId, FolderAccess access, CancellationToken cancellationToken)
    {
        if (!ImapMessageIds.TryParse(messageId, out string folderName, out uint validity, out uint uid))
            return MailErrors.MessageNotFound(messageId);

        Result<IMailFolder> folder = await FolderAsync(client, folderName, cancellationToken);
        if (folder.IsFailure)
            return MailErrors.MessageNotFound(messageId);

        await folder.Value.OpenAsync(access, cancellationToken);
        return folder.Value.UidValidity == validity ? (folder.Value, new UniqueId(uid)) : MailErrors.MessageNotFound(messageId);
    }

    private static async Task<Result<MailMessage>> LoadAsync(IMailFolder folder, UniqueId uid, string id, CancellationToken cancellationToken)
    {
        MimeMessage message;
        try
        {
            message = await folder.GetMessageAsync(uid, cancellationToken);
        }
        catch (MessageNotFoundException)
        {
            return MailErrors.MessageNotFound(id);
        }
        catch (ImapCommandException)
        {
            return MailErrors.MessageNotFound(id);
        }

        string body = message.TextBody ?? (message.HtmlBody is { } html ? HtmlText.Convert(html) : "");
        return new MailMessage(
            id,
            folder.FullName,
            Address(message.From),
            [.. message.To.Mailboxes.Select(mailbox => mailbox.Address)],
            [.. message.Cc.Mailboxes.Select(mailbox => mailbox.Address)],
            message.Subject ?? "",
            message.Date,
            body,
            [.. message.Attachments.Select(attachment => attachment is MimePart part ? part.FileName ?? "attachment" : "attached message")],
            message.MessageId,
            message.InReplyTo,
            [.. message.References]);
    }

    private static async Task<List<IMailFolder>> ThreadFoldersAsync(ImapClient client, IMailFolder current, CancellationToken cancellationToken)
    {
        var folders = new List<IMailFolder> { current, client.Inbox };
        if (await SpecialFolderAsync(client, SpecialFolder.Sent, cancellationToken) is { } sent)
            folders.Add(sent);
        return [.. folders.DistinctBy(folder => folder.FullName)];
    }

    private static async Task<IMailFolder?> SpecialFolderAsync(ImapClient client, SpecialFolder kind, CancellationToken cancellationToken)
    {
        if (!client.Capabilities.HasFlag(ImapCapabilities.SpecialUse) && !client.Capabilities.HasFlag(ImapCapabilities.XList))
            return null;

        // MailKit fills the special folders from the LIST responses of the personal namespace.
        if (client.PersonalNamespaces.Count > 0)
            await client.GetFoldersAsync(client.PersonalNamespaces[0], StatusItems.None, subscribedOnly: false, cancellationToken);
        return client.GetFolder(kind);
    }

    private static async Task<Result<IMailFolder>> DraftsFolderAsync(ImapClient client, MailAccount account, CancellationToken cancellationToken)
    {
        if (await SpecialFolderAsync(client, SpecialFolder.Drafts, cancellationToken) is { } special)
            return Result.Success(special);
        if (string.IsNullOrWhiteSpace(account.Settings.DraftsFolder))
            return MailErrors.DraftsFolderMissing(account.Alias);

        Result<IMailFolder> configured = await FolderAsync(client, account.Settings.DraftsFolder, cancellationToken);
        return configured.IsSuccess ? configured : MailErrors.DraftsFolderMissing(account.Alias);
    }

    private static async Task<Result<string>> AppendDraftAsync(ImapClient client, MailAccount account, IMailFolder drafts, MailDraft draft, CancellationToken cancellationToken)
    {
        var message = new MimeMessage { Subject = draft.Subject, Date = DateTimeOffset.UtcNow, Body = new TextPart("plain") { Text = draft.Body } };
        message.MessageId = MimeKit.Utils.MimeUtils.GenerateMessageId(DomainOf(account.Settings.UserName));
        message.From.Add(MailboxAddress.Parse(account.Settings.UserName));
        foreach (string recipient in draft.To)
        {
            if (!MailboxAddress.TryParse(recipient, out MailboxAddress? address))
                return new Error("mail.invalid_recipient", $"'{recipient}' is not an email address.", ErrorType.Validation);
            message.To.Add(address);
        }

        if (draft.ReplyTo is { } replyTo)
        {
            Result<(IMailFolder Folder, UniqueId Uid)> original = await LocateAsync(client, replyTo, FolderAccess.ReadOnly, cancellationToken);
            if (original.IsFailure)
                return original.Error;

            Result<MailMessage> answered = await LoadAsync(original.Value.Folder, original.Value.Uid, replyTo, cancellationToken);
            if (answered.IsFailure)
                return answered.Error;
            if (answered.Value.MessageId is { } parent)
            {
                message.InReplyTo = parent;
                foreach (string reference in answered.Value.References.Append(parent))
                    message.References.Add(reference);
            }
        }

        await drafts.OpenAsync(FolderAccess.ReadWrite, cancellationToken);
        UniqueId? appended = await drafts.AppendAsync(new AppendRequest(message, MessageFlags.Draft | MessageFlags.Seen), cancellationToken);
        if (appended is null)
        {
            IList<UniqueId> found = await drafts.SearchAsync(SearchQuery.HeaderContains("Message-ID", message.MessageId), cancellationToken);
            if (found.Count == 0)
                return MailErrors.MessageNotFound(message.MessageId);
            appended = found[^1];
        }

        return ImapMessageIds.Format(drafts.FullName, drafts.UidValidity, appended.Value.Id);
    }

    private async Task<Result<T>> WithClientAsync<T>(MailAccount account, Func<ImapClient, Task<Result<T>>> work, CancellationToken cancellationToken)
    {
        Result<ImapClient> connected = await _connector.ConnectAsync(account, cancellationToken);
        if (connected.IsFailure)
            return connected.Error;

        using ImapClient client = connected.Value;
        try
        {
            Result<T> result = await work(client);
            await client.DisconnectAsync(quit: true, cancellationToken);
            return result;
        }
        catch (Exception exception) when (ImapConnector.IsConnectionFailure(exception))
        {
            return MailErrors.Unreachable(account.Alias, exception.Message);
        }
    }

    private static string? SpecialUse(FolderAttributes attributes) => attributes switch
    {
        _ when attributes.HasFlag(FolderAttributes.Drafts) => "\\Drafts",
        _ when attributes.HasFlag(FolderAttributes.Sent) => "\\Sent",
        _ when attributes.HasFlag(FolderAttributes.Trash) => "\\Trash",
        _ when attributes.HasFlag(FolderAttributes.Junk) => "\\Junk",
        _ when attributes.HasFlag(FolderAttributes.Archive) => "\\Archive",
        _ when attributes.HasFlag(FolderAttributes.All) => "\\All",
        _ when attributes.HasFlag(FolderAttributes.Flagged) => "\\Flagged",
        _ => null,
    };

    private static string Address(InternetAddressList addresses) =>
        addresses.Mailboxes.FirstOrDefault()?.Address ?? addresses.FirstOrDefault()?.ToString() ?? "";

    private static string DomainOf(string address) => address.Contains('@', StringComparison.Ordinal) ? address[(address.IndexOf('@', StringComparison.Ordinal) + 1)..] : "marshal.local";

    private static Error NotADraft(string id) => new("mail.not_a_draft", $"'{id}' is not a draft Marshal can replace.", ErrorType.Validation);
}
