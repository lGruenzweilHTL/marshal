using Marshal.Abstractions.Audit;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Tokens;
using Marshal.Api.Cli;
using Marshal.Core.Execution;
using Marshal.Data;
using Marshal.Data.Audit;
using Marshal.Data.Identity;
using Marshal.Data.Persistence;
using Marshal.Data.Tokens;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Marshal.Api.Tests.Cli;

public sealed class TokenCommandTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 28, 10, 0, 0, TimeSpan.Zero));
    private readonly AsyncLocalExecutionContextAccessor _accessor = new();
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();

    [Fact]
    public async Task Create_PrintsTokenOnceStoresHashAndAudits()
    {
        await using MarshalDbContext context = await ContextAsync();

        int exit = await Command().RunAsync(["create", "--name", "ha", "--scope", "conversation:write"], Ct);

        Assert.Equal(0, exit);
        string printed = _output.ToString();
        string token = printed.Split('\n').Single(line => line.StartsWith("mrs_", StringComparison.Ordinal)).Trim();
        OwnerId owner = await new EfInstanceOwner(Factory).GetAsync(Ct);
        using (_accessor.Use(new Abstractions.Execution.MarshalExecutionContext { OwnerId = owner, Source = Abstractions.Execution.SessionSource.Api }))
        {
            ApiTokenInfo info = Assert.Single(await new ApiTokenService(Factory, _accessor, _time).ListAsync(Ct));
            Assert.Equal("ha", info.Name);
            Assert.Equal([ApiScopes.ConversationWrite], info.Scopes);
            Assert.Equal(owner, info.OwnerId);
            AuditEvent audit = Assert.Single(await context.AuditEvents.ToListAsync(Ct));
            Assert.Equal("token.create", audit.Action);
            Assert.Equal(AuditActorKind.System, audit.ActorKind);
            Assert.Equal(info.Id.ToString(), audit.Target);
            Assert.DoesNotContain(token, audit.Details ?? "", StringComparison.Ordinal);
        }

        Assert.DoesNotContain(token, await AllTextInDatabaseAsync(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("hooks:uptime-kuma")]
    [InlineData("hooks:proxmox")]
    public async Task Create_HookScope(string scope)
    {
        await using MarshalDbContext context = await ContextAsync();

        int exit = await Command().RunAsync(["create", "--name", "hook", "--scope", scope], Ct);

        Assert.Equal(0, exit);
        OwnerId owner = await new EfInstanceOwner(Factory).GetAsync(Ct);
        using (_accessor.Use(new Abstractions.Execution.MarshalExecutionContext { OwnerId = owner, Source = Abstractions.Execution.SessionSource.Api }))
            Assert.Equal([scope], Assert.Single(await new ApiTokenService(Factory, _accessor, _time).ListAsync(Ct)).Scopes);
    }

    [Fact]
    public async Task Create_SeveralScopes()
    {
        await using MarshalDbContext context = await ContextAsync();

        await Command().RunAsync(["create", "--name", "api", "--scope", "tasks:read", "--scope", "conversation:write"], Ct);

        Assert.Contains("tasks:read, conversation:write", _output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Create_UnknownScope_FailsWithMessage()
    {
        await using MarshalDbContext context = await ContextAsync();

        int exit = await Command().RunAsync(["create", "--name", "x", "--scope", "root"], Ct);

        Assert.Equal(1, exit);
        Assert.Contains("Unknown scope 'root'", _error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task List_ShowsTokensWithoutSecrets()
    {
        await using MarshalDbContext context = await ContextAsync();
        await Command().RunAsync(["create", "--name", "ha", "--scope", "conversation:write"], Ct);
        string token = _output.ToString().Split('\n').Single(line => line.StartsWith("mrs_", StringComparison.Ordinal)).Trim();
        _output.GetStringBuilder().Clear();

        int exit = await Command().RunAsync(["list"], Ct);

        Assert.Equal(0, exit);
        Assert.Contains("ha", _output.ToString(), StringComparison.Ordinal);
        Assert.Contains("conversation:write", _output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(token, _output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Revoke_MarksRevokedAndAudits()
    {
        await using MarshalDbContext context = await ContextAsync();
        await Command().RunAsync(["create", "--name", "ha", "--scope", "conversation:write"], Ct);
        string id = (await context.ApiTokens.IgnoreQueryFilters().SingleAsync(Ct)).Id.ToString();

        int exit = await Command().RunAsync(["revoke", id], Ct);

        Assert.Equal(0, exit);
        Assert.NotNull((await context.ApiTokens.IgnoreQueryFilters().AsNoTracking().SingleAsync(Ct)).RevokedAt);
        Assert.Contains(await context.AuditEvents.IgnoreQueryFilters().Select(audit => audit.Action).ToListAsync(Ct), action => action == "token.revoke");
    }

    [Theory]
    [InlineData]
    [InlineData("create", "--name", "x")]
    [InlineData("create", "--scope", "admin")]
    [InlineData("revoke")]
    [InlineData("revoke", "not-a-guid")]
    [InlineData("rotate")]
    public async Task InvalidUsage_ExitCode2(params string[] args)
    {
        await using MarshalDbContext context = await ContextAsync();

        int exit = await Command().RunAsync(args, Ct);

        Assert.Equal(2, exit);
        Assert.Contains("Usage", _error.ToString(), StringComparison.Ordinal);
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync();
        await _output.DisposeAsync();
        await _error.DisposeAsync();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private IDbContextFactory<MarshalDbContext> Factory => new ContextFactory(_connection, _accessor);

    private TokenCommand Command() =>
        new(new ApiTokenService(Factory, _accessor, _time), new DbAuditLog(Factory, _accessor, _time), new EfInstanceOwner(Factory), _accessor, _time, _output, _error);

    private async Task<MarshalDbContext> ContextAsync()
    {
        await _connection.OpenAsync(Ct);
        var context = new MarshalDbContext(new DbContextOptionsBuilder<MarshalDbContext>().UseMarshalSqlite(_connection).Options, _accessor);
        await new DatabaseInitializer(context, _time, NullLogger<DatabaseInitializer>.Instance).InitializeAsync(new InstanceOwnerSeed("Lukas", "de", "Europe/Vienna"), Ct);
        return context;
    }

    private sealed class ContextFactory(SqliteConnection connection, AsyncLocalExecutionContextAccessor accessor) : IDbContextFactory<MarshalDbContext>
    {
        public MarshalDbContext CreateDbContext() =>
            new(new DbContextOptionsBuilder<MarshalDbContext>().UseMarshalSqlite(connection).Options, accessor);
    }

    private async Task<string> AllTextInDatabaseAsync()
    {
        await using SqliteCommand command = _connection.CreateCommand();
        var text = new System.Text.StringBuilder();
        foreach (string table in new[] { "api_tokens", "audit_events" })
        {
            command.CommandText = $"SELECT * FROM {table}";
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(Ct);
            while (await reader.ReadAsync(Ct))
            {
                for (int i = 0; i < reader.FieldCount; i++)
                    text.Append(reader.GetValue(i)).Append('|');
            }
        }

        return text.ToString();
    }
}
