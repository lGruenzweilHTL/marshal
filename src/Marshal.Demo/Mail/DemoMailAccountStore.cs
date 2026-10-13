using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Mail;

namespace Marshal.Demo.Mail;

/// <summary>The fixed demo mail account; its server settings name a host that is never contacted.</summary>
public static class DemoMailAccounts
{
    public const string Alias = "demo";

    public static readonly MailAccountId Id = new(Guid.Parse("00000000-0000-7000-8000-0000000de300"));

    public static bool Is(MailAccount account) => account.Id == Id;

    internal static MailAccount For(OwnerId owner, DemoWorld world) => new(
        Id,
        owner,
        Alias,
        MailProviderKind.Imap,
        MailCredentialStrategy.Password,
        "demo_mail",
        new MailAccountSettings("demo.invalid", 993, MailSecurity.Tls, world.Mail.Address, DemoMailbox.DraftsFolder),
        world.Started);
}

/// <summary>
/// Shows the demo account while the owner has no mail account of their own; with a real account the demo account
/// disappears and every call goes to the real store. The demo account cannot be deleted.
/// </summary>
internal sealed class DemoMailAccountStore(IMailAccountStore inner, IExecutionContextAccessor executionContext, DemoWorld world) : IMailAccountStore
{
    public async Task<IReadOnlyList<MailAccount>> ListAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<MailAccount> accounts = await inner.ListAsync(cancellationToken);
        return accounts.Count == 0 && executionContext.Current is { } current ? [DemoMailAccounts.For(current.OwnerId, world)] : accounts;
    }

    public async Task<MailAccount?> GetAsync(MailAccountId id, CancellationToken cancellationToken) =>
        id == DemoMailAccounts.Id ? await DemoAsync(cancellationToken) : await inner.GetAsync(id, cancellationToken);

    public async Task<MailAccount?> FindByAliasAsync(string alias, CancellationToken cancellationToken) =>
        await inner.FindByAliasAsync(alias, cancellationToken)
        ?? (string.Equals(alias.Trim(), DemoMailAccounts.Alias, StringComparison.OrdinalIgnoreCase) ? await DemoAsync(cancellationToken) : null);

    public Task<Result<MailAccount>> CreateAsync(NewMailAccount account, CancellationToken cancellationToken) => inner.CreateAsync(account, cancellationToken);

    public Task<Result> DeleteAsync(MailAccountId id, CancellationToken cancellationToken) =>
        id == DemoMailAccounts.Id ? Task.FromResult<Result>(MailErrors.AccountNotFound(DemoMailAccounts.Alias)) : inner.DeleteAsync(id, cancellationToken);

    private async Task<MailAccount?> DemoAsync(CancellationToken cancellationToken) =>
        (await ListAsync(cancellationToken)).FirstOrDefault(DemoMailAccounts.Is);
}
