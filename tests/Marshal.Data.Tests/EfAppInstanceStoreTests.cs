using Marshal.Abstractions.Apps;
using Marshal.Abstractions.Runs;
using Marshal.Data.Apps;
using Marshal.Testing;
using Microsoft.Extensions.Time.Testing;
using static Marshal.Testing.TestExecutionContext;

namespace Marshal.Data.Tests;

public sealed class EfAppInstanceStoreTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 17, 8, 0, 0, TimeSpan.Zero));

    private static NewAppInstance Instance(Abstractions.Identity.OwnerId owner, string? result = """{"content":[]}""") =>
        new(owner, null, RunId.New(), "call-1", """{"month":"9"}""", new McpAppOutput("charts", "sales_report", "ui://charts/report", "<p>chart</p>"), result);

    [Fact]
    public async Task Create_ThenGet_ReturnsInstance()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        await using MarshalDbContext context = database.CreateContext(OwnerA);
        var store = new EfAppInstanceStore(database.Factory, _time);

        AppInstanceId id = await store.CreateAsync(Instance(OwnerA), Ct);
        AppInstanceRecord? loaded = await store.GetAsync(id, Ct);

        Assert.NotNull(loaded);
        Assert.Equal(("charts", "sales_report", "call-1", "ui://charts/report", "<p>chart</p>"), (loaded.Server, loaded.Tool, loaded.ToolCallId, loaded.ResourceUri, loaded.Html));
        Assert.Equal("""{"month":"9"}""", loaded.Arguments);
        Assert.Equal("""{"content":[]}""", loaded.Result);
        Assert.Equal(_time.GetUtcNow(), loaded.CreatedAt);
    }

    [Fact]
    public async Task Get_OtherOwnersInstance_IsNull()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        await using MarshalDbContext context = database.CreateContext(OwnerA);
        var store = new EfAppInstanceStore(database.Factory, _time);
        AppInstanceId id = await store.CreateAsync(Instance(OwnerA), Ct);

        database.Accessor.Current = Text(OwnerB);

        Assert.Null(await store.GetAsync(id, Ct));
    }

    [Fact]
    public async Task Create_ResultTooLarge_StoredWithoutResult()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        await using MarshalDbContext context = database.CreateContext(OwnerA);
        var store = new EfAppInstanceStore(database.Factory, _time);

        AppInstanceId id = await store.CreateAsync(Instance(OwnerA, result: null), Ct);

        Assert.Null((await store.GetAsync(id, Ct))!.Result);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
}
