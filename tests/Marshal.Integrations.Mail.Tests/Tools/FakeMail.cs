using Marshal.Abstractions;
using Marshal.Abstractions.Mail;
using Marshal.Testing;

namespace Marshal.Integrations.Mail.Tests.Tools;

/// <summary>Accounts in a list (no owner scoping needed in tool tests).</summary>
internal sealed class FakeMailAccountStore : IMailAccountStore
{
    public List<MailAccount> Accounts { get; } = [];

    public MailAccount Add(string alias)
    {
        var account = new MailAccount(MailAccountId.New(), TestExecutionContext.OwnerA, alias, MailProviderKind.Imap, MailCredentialStrategy.Password, $"mail_{alias}_password",
            new MailAccountSettings("imap.example.com", 993, MailSecurity.Tls, $"lukas@{alias}.example.com"), DateTimeOffset.UnixEpoch);
        Accounts.Add(account);
        return account;
    }

    public Task<IReadOnlyList<MailAccount>> ListAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<MailAccount>>([.. Accounts]);

    public Task<MailAccount?> GetAsync(MailAccountId id, CancellationToken cancellationToken) => Task.FromResult(Accounts.FirstOrDefault(account => account.Id == id));

    public Task<MailAccount?> FindByAliasAsync(string alias, CancellationToken cancellationToken) =>
        Task.FromResult(Accounts.FirstOrDefault(account => string.Equals(account.Alias, alias, StringComparison.OrdinalIgnoreCase)));

    public Task<Result<MailAccount>> CreateAsync(NewMailAccount account, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<Result> DeleteAsync(MailAccountId id, CancellationToken cancellationToken) => throw new NotSupportedException();
}

/// <summary>Answers from per-account data and records calls.</summary>
internal sealed class FakeMailProvider(MailCapabilities capabilities = MailCapabilities.None) : IMailProvider
{
    public Dictionary<string, List<MailMessage>> Messages { get; } = [];

    public List<(string Alias, MailQuery Query)> Searches { get; } = [];

    public List<(string Alias, MailDraft Draft, string? ReplacedId)> Drafts { get; } = [];

    public MailProviderKind Kind => MailProviderKind.Imap;

    public MailCapabilities Capabilities => capabilities;

    public MailMessage Add(string alias, string id, string subject, string body, string? messageId = null)
    {
        var message = new MailMessage(id, "INBOX", "alice@example.com", ["lukas@example.com"], [], subject, new DateTimeOffset(2026, 9, 20, 8, 0, 0, TimeSpan.Zero).AddMinutes(Messages.Values.Sum(list => list.Count)),
            body, ["plan.pdf"], messageId, null, []);
        if (!Messages.TryGetValue(alias, out List<MailMessage>? list))
            Messages[alias] = list = [];
        list.Add(message);
        return message;
    }

    public Task<Result<IReadOnlyList<MailSummary>>> SearchAsync(MailAccount account, MailQuery query, CancellationToken cancellationToken)
    {
        Searches.Add((account.Alias, query));
        IReadOnlyList<MailSummary> hits = [.. (Messages.GetValueOrDefault(account.Alias) ?? [])
            .Where(message => query.Subject is null || message.Subject.Contains(query.Subject, StringComparison.OrdinalIgnoreCase))
            .Select(message => new MailSummary(message.Id, message.Folder, message.From, message.To, message.Subject, message.Date, Unread: true))];
        return Task.FromResult(Result.Success(hits));
    }

    public Task<Result<MailMessage>> ReadMessageAsync(MailAccount account, string messageId, CancellationToken cancellationToken) =>
        Task.FromResult((Messages.GetValueOrDefault(account.Alias) ?? []).FirstOrDefault(message => message.Id == messageId) is { } found
            ? Result.Success(found)
            : Result.Failure<MailMessage>(MailErrors.MessageNotFound(messageId)));

    public Task<Result<IReadOnlyList<MailMessage>>> ReadThreadAsync(MailAccount account, string messageId, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Success<IReadOnlyList<MailMessage>>([.. Messages.GetValueOrDefault(account.Alias) ?? []]));

    public Task<Result<IReadOnlyList<MailFolder>>> ListFoldersAsync(MailAccount account, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Success<IReadOnlyList<MailFolder>>([new MailFolder("INBOX", null), new MailFolder("Entwürfe", "\\Drafts")]));

    public Task<Result<IReadOnlyList<string>>> ListLabelsAsync(MailAccount account, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Success<IReadOnlyList<string>>(["Receipts"]));

    public Task<Result<string>> CreateDraftAsync(MailAccount account, MailDraft draft, CancellationToken cancellationToken)
    {
        Drafts.Add((account.Alias, draft, null));
        return Task.FromResult(Result.Success($"draft-{Drafts.Count}"));
    }

    public Task<Result<string>> UpdateDraftAsync(MailAccount account, string draftId, MailDraft draft, CancellationToken cancellationToken)
    {
        Drafts.Add((account.Alias, draft, draftId));
        return Task.FromResult(Result.Success($"draft-{Drafts.Count}"));
    }
}
