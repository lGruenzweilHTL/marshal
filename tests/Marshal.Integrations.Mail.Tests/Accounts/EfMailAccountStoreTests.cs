using Marshal.Abstractions;
using Marshal.Abstractions.Mail;
using Marshal.Integrations.Mail.Accounts;
using Marshal.Testing;
using Microsoft.Extensions.Time.Testing;

namespace Marshal.Integrations.Mail.Tests.Accounts;

public sealed class EfMailAccountStoreTests : IAsyncLifetime
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 29, 10, 0, 0, TimeSpan.Zero));
    private TestDatabase _database = null!;
    private EfMailAccountStore _store = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _database = await TestDatabase.CreateAsync();
        _database.Accessor.Current = TestExecutionContext.Text(TestExecutionContext.OwnerA);
        _store = new EfMailAccountStore(_database.Factory, _database.Accessor, _time);
    }

    public async ValueTask DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task Create_ListGetAndFindByAlias()
    {
        MailAccount created = (await _store.CreateAsync(Private(), Ct)).Value;

        Assert.Equal(("private", TestExecutionContext.OwnerA, _time.GetUtcNow()), (created.Alias, created.Owner, created.CreatedAt));
        Assert.Equal(created, Assert.Single(await _store.ListAsync(Ct)));
        Assert.Equal(created, await _store.GetAsync(created.Id, Ct));
        Assert.Equal(created, await _store.FindByAliasAsync("PRIVATE", Ct));
        Assert.Null(await _store.FindByAliasAsync("work", Ct));
    }

    [Fact]
    public async Task Create_AliasTakenOrInvalid()
    {
        await _store.CreateAsync(Private(), Ct);

        Assert.Equal("mail.alias_taken", (await _store.CreateAsync(Private(), Ct)).Error.Code);
        Assert.Equal("mail.invalid_alias", (await _store.CreateAsync(Private() with { Alias = "Work Mail" }, Ct)).Error.Code);
    }

    [Fact]
    public async Task Owners_SeeOnlyTheirAccountsAndMayReuseAliases()
    {
        MailAccount alice = (await _store.CreateAsync(Private(), Ct)).Value;
        _database.Accessor.Current = TestExecutionContext.Text(TestExecutionContext.OwnerB);

        Assert.Empty(await _store.ListAsync(Ct));
        Assert.Null(await _store.GetAsync(alice.Id, Ct));
        Assert.Null(await _store.FindByAliasAsync("private", Ct));
        Assert.Equal("mail.account_not_found", (await _store.DeleteAsync(alice.Id, Ct)).Error.Code);
        Assert.True((await _store.CreateAsync(Private(), Ct)).IsSuccess);
    }

    [Fact]
    public async Task Delete_RemovesTheAccount()
    {
        MailAccount created = (await _store.CreateAsync(Private(), Ct)).Value;

        Result deleted = await _store.DeleteAsync(created.Id, Ct);

        Assert.True(deleted.IsSuccess);
        Assert.Empty(await _store.ListAsync(Ct));
    }

    private static NewMailAccount Private() => new(
        "private", MailProviderKind.Imap, MailCredentialStrategy.Password, "mail_private_password",
        new MailAccountSettings("imap.example.com", 993, MailSecurity.Tls, "lukas@example.com", "Entwürfe"));
}
