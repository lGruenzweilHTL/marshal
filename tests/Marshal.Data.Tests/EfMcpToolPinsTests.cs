using Marshal.Abstractions;
using Marshal.Abstractions.Mcp;
using Marshal.Abstractions.Tools;
using Marshal.Data.Mcp;
using Marshal.Testing;
using Microsoft.EntityFrameworkCore;

namespace Marshal.Data.Tests;

public sealed class EfMcpToolPinsTests
{
    private static readonly DateTimeOffset _now = new(2026, 10, 16, 8, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Table_HasNoOwnerColumn()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        await using MarshalDbContext context = database.CreateContext();

        Microsoft.EntityFrameworkCore.Metadata.IEntityType entity = context.Model.FindEntityType(typeof(McpToolPinRow))!;

        Assert.Equal("mcp_tool_pins", entity.GetTableName());
        Assert.DoesNotContain(entity.GetProperties(), property => property.GetColumnName() == "owner_id");
    }

    [Fact]
    public async Task SaveAndGet_WithoutExecutionContext()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        database.Accessor.Current = null;
        var pins = new EfMcpToolPins(database.Factory);

        await pins.SaveAsync([Pin("search"), Pin("get") with { State = ToolPinState.Pending, ApprovedFingerprint = null }, Pin("other", server: "ha")], Ct);

        IReadOnlyList<McpToolPin> stored = await pins.GetAsync("paperless", Ct);
        Assert.Equal(["get", "search"], stored.Select(pin => pin.ToolName));
        Assert.Equal(Pin("search"), stored[1]);
        McpToolPin pending = Assert.Single(await pins.ListPendingAsync(Ct));
        Assert.Equal(("paperless", "get", McpToolChange.New), (pending.ServerId, pending.ToolName, pending.Change));
    }

    [Fact]
    public async Task Save_ExistingPin_IsReplaced()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        var pins = new EfMcpToolPins(database.Factory);
        await pins.SaveAsync([Pin("search")], Ct);

        McpToolPin changed = Pin("search") with { Fingerprint = "f2", State = ToolPinState.Pending, PreviousDefinitionJson = "{\"name\":\"search\"}", CurrentDefinitionJson = "{\"description\":\"new\",\"name\":\"search\"}" };
        await pins.SaveAsync([changed], Ct);

        McpToolPin stored = Assert.Single(await pins.GetAsync("paperless", Ct));
        Assert.Equal(changed, stored);
        Assert.Equal(McpToolChange.Changed, stored.Change);
    }

    [Fact]
    public async Task Decide_ApproveTakesTheCurrentFingerprint()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        var pins = new EfMcpToolPins(database.Factory);
        await pins.SaveAsync([Pin("search") with { Fingerprint = "f2", State = ToolPinState.Pending, PreviousDefinitionJson = "{}" }], Ct);

        Result<McpToolPin> decided = await pins.DecideAsync("paperless", "search", approve: true, TestExecutionContext.OwnerA, _now, Ct);

        Assert.Equal((ToolPinState.Approved, "f2", null, TestExecutionContext.OwnerA, _now), (decided.Value.State, decided.Value.ApprovedFingerprint, decided.Value.PreviousDefinitionJson, decided.Value.DecidedBy, decided.Value.DecidedAt));
        Assert.Empty(await pins.ListPendingAsync(Ct));
    }

    [Fact]
    public async Task Decide_RejectKeepsTheApprovedFingerprint()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        var pins = new EfMcpToolPins(database.Factory);
        await pins.SaveAsync([Pin("search") with { Fingerprint = "f2", State = ToolPinState.Pending }], Ct);

        Result<McpToolPin> decided = await pins.DecideAsync("paperless", "search", approve: false, TestExecutionContext.OwnerA, _now, Ct);

        Assert.Equal((ToolPinState.Rejected, "f1"), (decided.Value.State, decided.Value.ApprovedFingerprint));
    }

    [Fact]
    public async Task Decide_NotPendingOrMissing_Fails()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        var pins = new EfMcpToolPins(database.Factory);
        await pins.SaveAsync([Pin("search")], Ct);

        Assert.Equal("mcp.pin_not_pending", (await pins.DecideAsync("paperless", "search", true, TestExecutionContext.OwnerA, _now, Ct)).Error.Code);
        Assert.Equal("mcp.pin_not_found", (await pins.DecideAsync("paperless", "nothing", true, TestExecutionContext.OwnerA, _now, Ct)).Error.Code);
    }

    [Fact]
    public async Task State_IsStoredSnakeCase()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        await new EfMcpToolPins(database.Factory).SaveAsync([Pin("search") with { State = ToolPinState.Pending }], Ct);

        await using MarshalDbContext context = database.CreateContext();
        string state = await context.Database.SqlQueryRaw<string>("SELECT state AS \"Value\" FROM mcp_tool_pins").SingleAsync(Ct);

        Assert.Equal("pending", state);
    }

    private static McpToolPin Pin(string tool, string server = "paperless") =>
        new(server, tool, "f1", ToolPinState.Approved, "f1", null, $"{{\"name\":\"{tool}\"}}", _now);
}
