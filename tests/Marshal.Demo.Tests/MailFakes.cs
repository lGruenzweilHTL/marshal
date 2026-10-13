using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Mail;

namespace Marshal.Demo.Tests;

/// <summary>Stands in for the IMAP provider and records every call, so tests prove the demo account never reaches it.</summary>
internal sealed class RecordingMailProvider : IMailProvider
{
    public List<string> Calls { get; } = [];

    public MailProviderKind Kind => MailProviderKind.Imap;

    public MailCapabilities Capabilities => MailCapabilities.None;

    public Task<Result<IReadOnlyList<MailSummary>>> SearchAsync(MailAccount account, MailQuery query, CancellationToken cancellationToken) =>
        Record(account, Result.Success<IReadOnlyList<MailSummary>>([]));

    public Task<Result<MailMessage>> ReadMessageAsync(MailAccount account, string messageId, CancellationToken cancellationToken) =>
        Record<MailMessage>(account, MailErrors.MessageNotFound(messageId));

    public Task<Result<IReadOnlyList<MailMessage>>> ReadThreadAsync(MailAccount account, string messageId, CancellationToken cancellationToken) =>
        Record<IReadOnlyList<MailMessage>>(account, MailErrors.MessageNotFound(messageId));

    public Task<Result<MailAttachment>> ReadAttachmentAsync(MailAccount account, string messageId, string attachment, CancellationToken cancellationToken) =>
        Record<MailAttachment>(account, MailErrors.MessageNotFound(messageId));

    public Task<Result<IReadOnlyList<MailFolder>>> ListFoldersAsync(MailAccount account, CancellationToken cancellationToken) =>
        Record(account, Result.Success<IReadOnlyList<MailFolder>>([]));

    public Task<Result<IReadOnlyList<string>>> ListLabelsAsync(MailAccount account, CancellationToken cancellationToken) =>
        Record(account, Result.Success<IReadOnlyList<string>>([]));

    public Task<Result<string>> CreateDraftAsync(MailAccount account, MailDraft draft, CancellationToken cancellationToken) =>
        Record<string>(account, "d1");

    public Task<Result<string>> UpdateDraftAsync(MailAccount account, string draftId, MailDraft draft, CancellationToken cancellationToken) =>
        Record<string>(account, draftId);

    private Task<Result<T>> Record<T>(MailAccount account, Result<T> result, [System.Runtime.CompilerServices.CallerMemberName] string call = "")
    {
        Calls.Add($"{account.Alias}:{call}");
        return Task.FromResult(result);
    }
}

/// <summary>The current owner's mail accounts in memory, in place of the database store.</summary>
internal sealed class InMemoryMailAccounts(IExecutionContextAccessor executionContext, TimeProvider time) : IMailAccountStore
{
    private readonly List<MailAccount> _accounts = [];

    public Task<IReadOnlyList<MailAccount>> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<MailAccount>>([.. _accounts.Where(account => account.Owner == executionContext.Current?.OwnerId)]);

    public async Task<MailAccount?> GetAsync(MailAccountId id, CancellationToken cancellationToken) =>
        (await ListAsync(cancellationToken)).FirstOrDefault(account => account.Id == id);

    public async Task<MailAccount?> FindByAliasAsync(string alias, CancellationToken cancellationToken) =>
        (await ListAsync(cancellationToken)).FirstOrDefault(account => string.Equals(account.Alias, alias, StringComparison.OrdinalIgnoreCase));

    public async Task<Result<MailAccount>> CreateAsync(NewMailAccount account, CancellationToken cancellationToken)
    {
        if (await FindByAliasAsync(account.Alias, cancellationToken) is not null)
            return MailErrors.AliasTaken(account.Alias);

        var created = new MailAccount(MailAccountId.New(), executionContext.Current!.OwnerId, account.Alias, account.Provider, account.Strategy, account.CredentialSecret, account.Settings, time.GetUtcNow());
        _accounts.Add(created);
        return created;
    }

    public Task<Result> DeleteAsync(MailAccountId id, CancellationToken cancellationToken) =>
        Task.FromResult(_accounts.RemoveAll(account => account.Id == id) > 0 ? Result.Success() : (Result)MailErrors.AccountNotFound(id.ToString()));
}
