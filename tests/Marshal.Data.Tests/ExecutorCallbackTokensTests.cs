using Marshal.Abstractions;
using Marshal.Abstractions.Tasks;
using Marshal.Abstractions.Tokens;
using Marshal.Data.Tasks;
using Marshal.Data.Tokens;
using Marshal.Testing;
using Microsoft.Extensions.Time.Testing;
using static Marshal.Testing.TestExecutionContext;

namespace Marshal.Data.Tests;

public sealed class ExecutorCallbackTokensTests : IAsyncLifetime
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 30, 10, 0, 0, TimeSpan.Zero));
    private readonly TaskId _task = TaskId.New();
    private TestDatabase _database = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _database = await TestDatabase.CreateAsync();
        _database.Accessor.Current = Text(OwnerA);
        await using MarshalDbContext context = _database.CreateContext(OwnerA);
        context.AgentTasks.Add(new AgentTask
        {
            Id = _task,
            OwnerId = OwnerA,
            CreatedBy = TaskCreator.Task,
            Kind = TaskKind.Agent,
            Goal = "Review the change.",
            TriggerKind = TaskTriggerKind.Now,
            TriggerSpec = "",
            CreatedAt = _time.GetUtcNow(),
            UpdatedAt = _time.GetUtcNow(),
        });
        await context.SaveChangesAsync(Ct);
    }

    public async ValueTask DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task Issue_CallbackKindWithoutScopes_BoundToTheTask()
    {
        var session = Guid.NewGuid();

        CreatedApiToken created = await Tokens().IssueAsync(_task, session, Ct);

        _database.Accessor.Current = null;
        ApiTokenInfo info = (await Service().ValidateAsync(created.Plaintext, Ct)).Value;
        Assert.Equal((ApiTokenKind.ExecutorCallback, (TaskId?)_task, OwnerA), (info.Kind, info.TaskId, info.OwnerId));
        Assert.Empty(info.Scopes);
        Assert.Equal($"executor session {session}", info.Name);
    }

    [Fact]
    public async Task Revoke_TokenNoLongerValidates()
    {
        CreatedApiToken created = await Tokens().IssueAsync(_task, Guid.NewGuid(), Ct);

        await Tokens().RevokeAsync(created.Info.Id, Ct);

        Result<ApiTokenInfo> validated = await Service().ValidateAsync(created.Plaintext, Ct);
        Assert.Equal("token.invalid", validated.Error.Code);
    }

    private ExecutorCallbackTokens Tokens() => new(_database.Factory, _database.Accessor, _time);

    private ApiTokenService Service() => new(_database.Factory, _database.Accessor, _time);
}
