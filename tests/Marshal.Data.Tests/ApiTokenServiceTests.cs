using Marshal.Testing;
using System.Security.Cryptography;
using System.Text;
using Marshal.Abstractions;
using Marshal.Abstractions.Tokens;
using Marshal.Data.Tokens;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using static Marshal.Testing.TestExecutionContext;

namespace Marshal.Data.Tests;

public sealed class ApiTokenServiceTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 28, 10, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Create_StoresOnlyHashAndReturnsPlaintextOnce()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        await using MarshalDbContext context = database.CreateContext(OwnerA);

        CreatedApiToken created = (await Service(database).CreateAsync("ha", [ApiScopes.ConversationWrite], null, Ct)).Value;

        ApiToken row = await context.ApiTokens.SingleAsync(Ct);
        Assert.StartsWith("mrs_", created.Plaintext, StringComparison.Ordinal);
        Assert.True(created.Plaintext.Length >= 40);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(created.Plaintext))), row.TokenHash);
        Assert.DoesNotContain(created.Plaintext, row.TokenHash, StringComparison.Ordinal);
        Assert.Equal([ApiScopes.ConversationWrite], row.Scopes);
        Assert.Equal(OwnerA, row.OwnerId);
        Assert.Equal(ApiTokenKind.Standard, row.Kind);
    }

    [Fact]
    public async Task Create_UnknownScope_Fails()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        await using MarshalDbContext context = database.CreateContext(OwnerA);

        Result<CreatedApiToken> result = await Service(database).CreateAsync("ha", ["everything"], null, Ct);

        Assert.Equal("token.invalid_scope", result.Error.Code);
    }

    [Fact]
    public async Task Create_NoScopeOrName_Fails()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        await using MarshalDbContext context = database.CreateContext(OwnerA);

        Assert.True((await Service(database).CreateAsync("ha", [], null, Ct)).IsFailure);
        Assert.True((await Service(database).CreateAsync(" ", [ApiScopes.TasksRead], null, Ct)).IsFailure);
    }

    [Fact]
    public async Task Validate_ValidToken_ReturnsOwnerAndScopesWithoutContext()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        string plaintext;
        await using (MarshalDbContext asB = database.CreateContext(OwnerB))
            plaintext = (await Service(database).CreateAsync("api", [ApiScopes.TasksRead, ApiScopes.ConversationWrite], null, Ct)).Value.Plaintext;

        database.Accessor.Current = null;
        await using MarshalDbContext context = database.CreateContext();
        Result<ApiTokenInfo> result = await Service(database).ValidateAsync(plaintext, Ct);

        Assert.Equal(OwnerB, result.Value.OwnerId);
        Assert.Equal([ApiScopes.TasksRead, ApiScopes.ConversationWrite], result.Value.Scopes);
    }

    [Theory]
    [InlineData("mrs_unknown")]
    [InlineData("")]
    public async Task Validate_UnknownToken_Rejected(string plaintext)
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        await using MarshalDbContext context = database.CreateContext();

        Assert.Equal("token.invalid", (await Service(database).ValidateAsync(plaintext, Ct)).Error.Code);
    }

    [Fact]
    public async Task Validate_RevokedToken_Rejected()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        await using MarshalDbContext context = database.CreateContext(OwnerA);
        ApiTokenService service = Service(database);
        CreatedApiToken created = (await service.CreateAsync("ha", [ApiScopes.ConversationWrite], null, Ct)).Value;

        Assert.True((await service.RevokeAsync(created.Info.Id, Ct)).IsSuccess);

        Assert.Equal("token.invalid", (await service.ValidateAsync(created.Plaintext, Ct)).Error.Code);
        Assert.Equal(_time.GetUtcNow(), (await service.ListAsync(Ct)).Single().RevokedAt);
    }

    [Fact]
    public async Task Validate_ExpiredToken_Rejected()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        await using MarshalDbContext context = database.CreateContext(OwnerA);
        ApiTokenService service = Service(database);
        CreatedApiToken created = (await service.CreateAsync("ha", [ApiScopes.ConversationWrite], _time.GetUtcNow().AddDays(1), Ct)).Value;
        Assert.True((await service.ValidateAsync(created.Plaintext, Ct)).IsSuccess);

        _time.Advance(TimeSpan.FromDays(1));

        Assert.Equal("token.invalid", (await service.ValidateAsync(created.Plaintext, Ct)).Error.Code);
    }

    [Fact]
    public async Task ListAndRevoke_OnlyOwnTokens()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        ApiTokenId others;
        await using (MarshalDbContext asA = database.CreateContext(OwnerA))
            others = (await Service(database).CreateAsync("a", [ApiScopes.TasksRead], null, Ct)).Value.Info.Id;

        await using MarshalDbContext asB = database.CreateContext(OwnerB);
        ApiTokenService service = Service(database);

        Assert.Empty(await service.ListAsync(Ct));
        Assert.Equal("token.not_found", (await service.RevokeAsync(others, Ct)).Error.Code);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ApiTokenService Service(TestDatabase database) => new(database.Factory, database.Accessor, _time);
}
