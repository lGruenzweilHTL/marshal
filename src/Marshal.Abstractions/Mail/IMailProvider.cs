namespace Marshal.Abstractions.Mail;

/// <summary>
/// Mailbox operations of one protocol. There is deliberately no way to send, delete, move or flag messages.
/// Message and draft ids are opaque strings valid for the account they came from.
/// </summary>
public interface IMailProvider
{
    MailProviderKind Kind { get; }

    MailCapabilities Capabilities { get; }

    /// <summary>Messages matching every given criterion, newest first.</summary>
    Task<Result<IReadOnlyList<MailSummary>>> SearchAsync(MailAccount account, MailQuery query, CancellationToken cancellationToken);

    /// <summary>A message as text; <c>mail.message_not_found</c> when the id is unknown or stale.</summary>
    Task<Result<MailMessage>> ReadMessageAsync(MailAccount account, string messageId, CancellationToken cancellationToken);

    /// <summary>The message's conversation, oldest first.</summary>
    Task<Result<IReadOnlyList<MailMessage>>> ReadThreadAsync(MailAccount account, string messageId, CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<MailFolder>>> ListFoldersAsync(MailAccount account, CancellationToken cancellationToken);

    /// <summary>Labels (only with <see cref="MailCapabilities.Labels"/>).</summary>
    Task<Result<IReadOnlyList<string>>> ListLabelsAsync(MailAccount account, CancellationToken cancellationToken);

    /// <summary>Stores a new draft and returns its id; <c>mail.drafts_folder_missing</c> without a drafts folder.</summary>
    Task<Result<string>> CreateDraftAsync(MailAccount account, MailDraft draft, CancellationToken cancellationToken);

    /// <summary>Replaces a draft created earlier and returns the new draft's id.</summary>
    Task<Result<string>> UpdateDraftAsync(MailAccount account, string draftId, MailDraft draft, CancellationToken cancellationToken);
}

/// <summary>What a provider can do natively; tools adapt to it.</summary>
[Flags]
public enum MailCapabilities
{
    None = 0,

    /// <summary>The server groups threads (otherwise they are rebuilt from headers).</summary>
    NativeThreads = 1,

    /// <summary>Messages carry labels (Gmail API).</summary>
    Labels = 2,

    /// <summary>The server accepts its own search syntax.</summary>
    ServerSearchSyntax = 4,

    /// <summary>The server pushes new-mail events.</summary>
    PushEvents = 8,

    /// <summary>The OAuth scope itself forbids sending.</summary>
    ScopeEnforcedNoSend = 16,
}

/// <summary>Search criteria; all given ones must match.</summary>
/// <param name="From">Sender contains.</param>
/// <param name="To">Recipient contains.</param>
/// <param name="Subject">Subject contains.</param>
/// <param name="Text">Headers or body contain.</param>
/// <param name="Since">Received on or after (date).</param>
/// <param name="Before">Received before (date).</param>
/// <param name="Unread">Only unread (true) or only read (false) messages.</param>
/// <param name="Folder">Folder (default: the inbox).</param>
/// <param name="Limit">Maximum results.</param>
public sealed record MailQuery(
    string? From = null,
    string? To = null,
    string? Subject = null,
    string? Text = null,
    DateOnly? Since = null,
    DateOnly? Before = null,
    bool? Unread = null,
    string? Folder = null,
    int Limit = 20);

/// <summary>A search hit.</summary>
public sealed record MailSummary(string Id, string Folder, string From, IReadOnlyList<string> To, string Subject, DateTimeOffset Date, bool Unread);

/// <summary>A message converted to text; attachments are listed by name only.</summary>
public sealed record MailMessage(
    string Id,
    string Folder,
    string From,
    IReadOnlyList<string> To,
    IReadOnlyList<string> Cc,
    string Subject,
    DateTimeOffset Date,
    string Body,
    IReadOnlyList<string> Attachments,
    string? MessageId,
    string? InReplyTo,
    IReadOnlyList<string> References);

/// <summary>A folder and its special use (e.g. <c>\Drafts</c>), if any.</summary>
public sealed record MailFolder(string Name, string? SpecialUse);

/// <summary>Draft content.</summary>
/// <param name="To">Recipients.</param>
/// <param name="Subject">Subject.</param>
/// <param name="Body">Plain-text body.</param>
/// <param name="ReplyTo">Id of the message this draft answers (sets In-Reply-To and References).</param>
public sealed record MailDraft(IReadOnlyList<string> To, string Subject, string Body, string? ReplyTo = null);
