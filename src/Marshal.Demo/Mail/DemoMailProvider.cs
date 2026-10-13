using System.Text;
using Marshal.Abstractions;
using Marshal.Abstractions.Mail;

namespace Marshal.Demo.Mail;

/// <summary>
/// Serves the demo account from <see cref="DemoWorld.Mail"/> and passes every other account to the real provider.
/// Like the real providers it can only read and store drafts.
/// </summary>
internal sealed class DemoMailProvider(IMailProvider inner, DemoWorld world) : IMailProvider
{
    private const string Inbox = "INBOX";

    private DemoMailbox Mailbox => world.Mail;

    public MailProviderKind Kind => inner.Kind;

    public MailCapabilities Capabilities => inner.Capabilities;

    public Task<Result<IReadOnlyList<MailSummary>>> SearchAsync(MailAccount account, MailQuery query, CancellationToken cancellationToken)
    {
        if (!DemoMailAccounts.Is(account))
            return inner.SearchAsync(account, query, cancellationToken);

        string folder = query.Folder ?? Inbox;
        List<MailSummary> hits = [.. Mailbox.Messages
            .Where(message => string.Equals(message.Folder, folder, StringComparison.OrdinalIgnoreCase))
            .Where(message => Contains(message.From, query.From)
                && Contains(string.Join(", ", message.To), query.To)
                && Contains(message.Subject, query.Subject)
                && Contains($"{message.From}\n{string.Join(", ", message.To)}\n{message.Subject}\n{message.Body}", query.Text)
                && (query.Since is not { } since || DateOnly.FromDateTime(message.Date.UtcDateTime) >= since)
                && (query.Before is not { } before || DateOnly.FromDateTime(message.Date.UtcDateTime) < before)
                && (query.Unread is not { } unread || Mailbox.IsUnread(message.Id) == unread))
            .OrderByDescending(message => message.Date)
            .Take(Math.Max(1, query.Limit))
            .Select(message => new MailSummary(message.Id, message.Folder, message.From, message.To, message.Subject, message.Date, Mailbox.IsUnread(message.Id)))];
        return Task.FromResult<Result<IReadOnlyList<MailSummary>>>(hits);
    }

    public Task<Result<MailMessage>> ReadMessageAsync(MailAccount account, string messageId, CancellationToken cancellationToken) =>
        DemoMailAccounts.Is(account) ? Task.FromResult(Find(messageId)) : inner.ReadMessageAsync(account, messageId, cancellationToken);

    public Task<Result<IReadOnlyList<MailMessage>>> ReadThreadAsync(MailAccount account, string messageId, CancellationToken cancellationToken)
    {
        if (!DemoMailAccounts.Is(account))
            return inner.ReadThreadAsync(account, messageId, cancellationToken);

        Result<MailMessage> target = Find(messageId);
        if (target.IsFailure)
            return Task.FromResult<Result<IReadOnlyList<MailMessage>>>(target.Error);

        // Rebuilt from the headers like the IMAP provider: every message linked by Message-ID, In-Reply-To or References.
        IReadOnlyList<MailMessage> all = Mailbox.Messages;
        var thread = new List<MailMessage> { target.Value };
        bool grown = true;
        while (grown)
        {
            HashSet<string> ids = [.. thread.SelectMany(Links)];
            List<MailMessage> added = [.. all.Where(message => !thread.Contains(message) && Links(message).Any(ids.Contains))];
            thread.AddRange(added);
            grown = added.Count > 0;
        }

        return Task.FromResult<Result<IReadOnlyList<MailMessage>>>(thread.OrderBy(message => message.Date).ToList());
    }

    public Task<Result<MailAttachment>> ReadAttachmentAsync(MailAccount account, string messageId, string attachment, CancellationToken cancellationToken)
    {
        if (!DemoMailAccounts.Is(account))
            return inner.ReadAttachmentAsync(account, messageId, attachment, cancellationToken);

        Result<MailMessage> message = Find(messageId);
        if (message.IsFailure)
            return Task.FromResult<Result<MailAttachment>>(message.Error);

        IReadOnlyList<string> names = message.Value.Attachments;
        string? name = names.FirstOrDefault(candidate => string.Equals(candidate, attachment.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? (int.TryParse(attachment, out int position) && position >= 1 && position <= names.Count ? names[position - 1] : null);
        return Task.FromResult<Result<MailAttachment>>(name is null
            ? MailErrors.AttachmentNotFound(attachment, names)
            : new MailAttachment(name, Placeholder(name, message.Value.Subject)));
    }

    public Task<Result<IReadOnlyList<MailFolder>>> ListFoldersAsync(MailAccount account, CancellationToken cancellationToken) =>
        DemoMailAccounts.Is(account) ? Task.FromResult<Result<IReadOnlyList<MailFolder>>>(Result.Success(Mailbox.Folders)) : inner.ListFoldersAsync(account, cancellationToken);

    public Task<Result<IReadOnlyList<string>>> ListLabelsAsync(MailAccount account, CancellationToken cancellationToken) =>
        DemoMailAccounts.Is(account) ? Task.FromResult<Result<IReadOnlyList<string>>>(Array.Empty<string>()) : inner.ListLabelsAsync(account, cancellationToken);

    public Task<Result<string>> CreateDraftAsync(MailAccount account, MailDraft draft, CancellationToken cancellationToken) =>
        DemoMailAccounts.Is(account) ? Task.FromResult<Result<string>>(Mailbox.AddDraft(draft, world.Time.GetUtcNow())) : inner.CreateDraftAsync(account, draft, cancellationToken);

    public Task<Result<string>> UpdateDraftAsync(MailAccount account, string draftId, MailDraft draft, CancellationToken cancellationToken)
    {
        if (!DemoMailAccounts.Is(account))
            return inner.UpdateDraftAsync(account, draftId, draft, cancellationToken);

        return Task.FromResult<Result<string>>(Mailbox.UpdateDraft(draftId, draft, world.Time.GetUtcNow()) ? draftId : MailErrors.MessageNotFound(draftId));
    }

    private Result<MailMessage> Find(string id) =>
        Mailbox.Messages.FirstOrDefault(message => message.Id == id) is { } message ? message : MailErrors.MessageNotFound(id);

    private static IEnumerable<string> Links(MailMessage message) =>
        new[] { message.MessageId, message.InReplyTo }.Concat(message.References).OfType<string>();

    private static bool Contains(string value, string? part) =>
        string.IsNullOrWhiteSpace(part) || value.Contains(part.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>A minimal one-page PDF for <c>.pdf</c> names, plain text otherwise.</summary>
    private static byte[] Placeholder(string name, string subject)
    {
        string text = $"Demo attachment {name} ({subject})";
        if (!name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            return Encoding.UTF8.GetBytes(text);

        string escaped = text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("(", "\\(", StringComparison.Ordinal).Replace(")", "\\)", StringComparison.Ordinal);
        string stream = $"BT /F1 12 Tf 72 720 Td ({escaped}) Tj ET";
        string[] objects =
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>",
            $"<< /Length {stream.Length} >>\nstream\n{stream}\nendstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
        ];
        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (int index = 0; index < objects.Length; index++)
        {
            offsets.Add(pdf.Length);
            pdf.Append(System.Globalization.CultureInfo.InvariantCulture, $"{index + 1} 0 obj\n{objects[index]}\nendobj\n");
        }

        int xref = pdf.Length;
        pdf.Append(System.Globalization.CultureInfo.InvariantCulture, $"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (int offset in offsets)
            pdf.Append(System.Globalization.CultureInfo.InvariantCulture, $"{offset:D10} 00000 n \n");
        pdf.Append(System.Globalization.CultureInfo.InvariantCulture, $"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(pdf.ToString());
    }
}
