using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Mail;
using Marshal.Data;
using Marshal.Data.Mail;
using Microsoft.EntityFrameworkCore;

namespace Marshal.Integrations.Mail.Accounts;

/// <summary>Mail accounts in the database, scoped to the current owner by the context's query filter.</summary>
public sealed class EfMailAccountStore(IDbContextFactory<MarshalDbContext> contexts, IExecutionContextAccessor executionContext, TimeProvider time) : IMailAccountStore
{
    public async Task<IReadOnlyList<MailAccount>> ListAsync(CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        List<StoredMailAccount> rows = await db.MailAccounts.AsNoTracking().OrderBy(row => row.Alias).ToListAsync(cancellationToken);
        return [.. rows.Select(ToAccount)];
    }

    public async Task<MailAccount?> GetAsync(MailAccountId id, CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        StoredMailAccount? row = await db.MailAccounts.AsNoTracking().FirstOrDefaultAsync(row => row.Id == id, cancellationToken);
        return row is null ? null : ToAccount(row);
    }

    public async Task<MailAccount?> FindByAliasAsync(string alias, CancellationToken cancellationToken)
    {
        string normalized = alias.Trim().ToLowerInvariant();
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        StoredMailAccount? row = await db.MailAccounts.AsNoTracking().FirstOrDefaultAsync(row => row.Alias == normalized, cancellationToken);
        return row is null ? null : ToAccount(row);
    }

    public async Task<Result<MailAccount>> CreateAsync(NewMailAccount account, CancellationToken cancellationToken)
    {
        Result<string> normalized = MailAccountAlias.Normalize(account.Alias);
        if (normalized.IsFailure)
            return normalized.Error;

        string alias = normalized.Value;

        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        if (await db.MailAccounts.AnyAsync(row => row.Alias == alias, cancellationToken))
            return MailErrors.AliasTaken(alias);

        var row = new StoredMailAccount
        {
            Id = MailAccountId.New(),
            OwnerId = executionContext.Current!.OwnerId,
            Alias = alias,
            Provider = account.Provider,
            Strategy = account.Strategy,
            CredentialSecret = account.CredentialSecret,
            Host = account.Settings.Host,
            Port = account.Settings.Port,
            Security = account.Settings.Security,
            UserName = account.Settings.UserName,
            DraftsFolder = string.IsNullOrWhiteSpace(account.Settings.DraftsFolder) ? null : account.Settings.DraftsFolder.Trim(),
            CreatedAt = time.GetUtcNow(),
        };
        db.MailAccounts.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        return ToAccount(row);
    }

    public async Task<Result> DeleteAsync(MailAccountId id, CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        int deleted = await db.MailAccounts.Where(row => row.Id == id).ExecuteDeleteAsync(cancellationToken);
        return deleted > 0 ? Result.Success() : MailErrors.AccountNotFound(id.ToString());
    }

    private static MailAccount ToAccount(StoredMailAccount row) => new(
        row.Id,
        row.OwnerId,
        row.Alias,
        row.Provider,
        row.Strategy,
        row.CredentialSecret,
        new MailAccountSettings(row.Host, row.Port, row.Security, row.UserName, row.DraftsFolder),
        row.CreatedAt);
}
