using Marshal.Abstractions.Mail;

namespace Marshal.Demo;

/// <summary>
/// The simulated mailbox of the demo account: sample messages and the drafts created during the process run.
/// There is no outgoing path; drafts stay in the Drafts folder.
/// </summary>
public sealed class DemoMailbox
{
    public const string DraftsFolder = "Drafts";

    private readonly Lock _gate = new();
    private readonly List<MailMessage> _messages;
    private readonly HashSet<string> _unread;

    internal DemoMailbox(DateTimeOffset started)
    {
        MailFile file = DemoResources.Read<MailFile>("mail.json");
        Address = file.Address;
        Folders = [.. file.Folders.Select(folder => new MailFolder(folder.Name, folder.SpecialUse))];
        _messages = [.. file.Messages.Select(message => new MailMessage(
            message.Id,
            message.Folder,
            message.From,
            message.To,
            message.Cc ?? [],
            message.Subject,
            started.AddMinutes(-message.MinutesAgo),
            message.Body,
            message.Attachments ?? [],
            $"<{message.Id}@demo.marshal.example>",
            null,
            []))];
        _unread = [.. file.Messages.Where(message => message.Unread).Select(message => message.Id)];
    }

    public string Address { get; }

    public IReadOnlyList<MailFolder> Folders { get; }

    public IReadOnlyList<MailMessage> Messages
    {
        get
        {
            lock (_gate)
                return [.. _messages];
        }
    }

    public bool IsUnread(string id)
    {
        lock (_gate)
            return _unread.Contains(id);
    }

    public string AddDraft(MailDraft draft, DateTimeOffset at)
    {
        lock (_gate)
        {
            string id = $"d{Guid.NewGuid():N}"[..9];
            _messages.Add(ToMessage(id, draft, at));
            return id;
        }
    }

    public bool UpdateDraft(string id, MailDraft draft, DateTimeOffset at)
    {
        lock (_gate)
        {
            int index = _messages.FindIndex(message => message.Id == id && message.Folder == DraftsFolder);
            if (index < 0)
                return false;

            _messages[index] = ToMessage(id, draft, at);
            return true;
        }
    }

    private MailMessage ToMessage(string id, MailDraft draft, DateTimeOffset at)
    {
        MailMessage? original = draft.ReplyTo is null ? null : _messages.FirstOrDefault(message => message.Id == draft.ReplyTo);
        return new MailMessage(id, DraftsFolder, Address, draft.To, [], draft.Subject, at, draft.Body, [], $"<{id}@demo.marshal.example>", original?.MessageId, original?.MessageId is { } parent ? [parent] : []);
    }

    private sealed record MailFile(string Address, List<FolderFile> Folders, List<MessageFile> Messages);

    private sealed record FolderFile(string Name, string? SpecialUse);

    private sealed record MessageFile(string Id, string Folder, string From, List<string> To, List<string>? Cc, string Subject, int MinutesAgo, bool Unread, string Body, List<string>? Attachments);
}
