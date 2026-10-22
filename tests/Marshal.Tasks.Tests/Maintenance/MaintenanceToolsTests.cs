using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Events;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Maintenance;
using Marshal.Abstractions.Tools;
using Marshal.Tasks.Maintenance;
using Marshal.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using static Marshal.Testing.TestExecutionContext;

namespace Marshal.Tasks.Tests.Maintenance;

public sealed class MaintenanceToolsTests : IAsyncLifetime
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 21, 8, 0, 0, TimeSpan.Zero));
    private readonly Dictionary<string, decimal> _values = new() { ["voron"] = 1200 };
    private TestDatabase _database = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _database = await TestDatabase.CreateAsync();

    public async ValueTask DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task NoItems_NoTools()
    {
        Assert.Empty(await Source(new MaintenanceOptions()).GetToolsAsync(Ct));
    }

    [Fact]
    public async Task Tools_SafeStatus_DonePersists_InTheMaintenanceNamespace()
    {
        IReadOnlyList<ITool> tools = await Source(Configured()).GetToolsAsync(Ct);

        Assert.Equal(["maintenance.status", "maintenance.done"], tools.Select(tool => tool.Descriptor.Id.ToString()));
        Assert.All(tools, tool => Assert.Equal(RiskTier.Safe, tool.Descriptor.DefaultTier));
        Assert.Equal([false, true], tools.Select(tool => tool.Descriptor.Persists));
        Assert.Contains("voron_nozzle (Voron nozzle)", tools[1].Descriptor.Description);
    }

    [Fact]
    public async Task Done_ResetsUsage_StatusShowsIt()
    {
        IReadOnlyList<ITool> tools = await Source(Configured()).GetToolsAsync(Ct);
        await InvokeAsync(tools[0], new { });
        _values["voron"] = 1500;

        JsonElement due = Assert.Single(JsonDocument.Parse((await InvokeAsync(tools[0], new { item = "voron_nozzle" })).Value.Content).RootElement.EnumerateArray());
        Assert.Equal(("due", 300m), (due.GetProperty("state").GetString(), due.GetProperty("usage").GetDecimal()));

        Result<ToolOutput> done = await InvokeAsync(tools[1], new { item = "voron_nozzle" });

        JsonElement status = JsonDocument.Parse(done.Value.Content).RootElement;
        Assert.Equal(("ok", 0m, "Voron nozzle", 300m, "h"), (status.GetProperty("state").GetString(), status.GetProperty("usage").GetDecimal(),
            status.GetProperty("name").GetString(), status.GetProperty("every").GetDecimal(), status.GetProperty("unit").GetString()));
        Assert.Equal(_time.GetUtcNow(), status.GetProperty("lastDoneAt").GetDateTimeOffset());
        Assert.Equal("Mark maintenance voron_nozzle as done", tools[1].Summarize(JsonSerializer.SerializeToElement(new { item = "voron_nozzle" }), "en"));
    }

    [Fact]
    public async Task UnknownItem_ListsTheItems()
    {
        IReadOnlyList<ITool> tools = await Source(Configured()).GetToolsAsync(Ct);

        Result<ToolOutput> done = await InvokeAsync(tools[1], new { item = "toaster" });

        Assert.Equal("maintenance.unknown_item", done.Error.Code);
        Assert.Contains("voron_nozzle", done.Error.Message);
    }

    private Task<Result<ToolOutput>> InvokeAsync(ITool tool, object arguments) =>
        tool.InvokeAsync(new ToolInvocation(JsonSerializer.SerializeToElement(arguments), Text(OwnerA)), Ct);

    private static MaintenanceOptions Configured() => new()
    {
        Items = { ["voron_nozzle"] = new() { Name = "Voron nozzle", Source = new() { Printer = "voron", Metric = MaintenanceMetric.PrintHours }, Every = 300, WarnBefore = 20 } },
    };

    private MaintenanceToolSource Source(MaintenanceOptions options) => new(new MaintenanceTracker(
        Options.Create(options), [new Counters(_values)], new Owner(), new FakeUserPreferencesStore(), new FakeNotificationSender(),
        _database.Factory, _database.Accessor, new NoEvents(), _time, NullLogger<MaintenanceTracker>.Instance));

    private sealed class Owner : IInstanceOwner
    {
        public Task<OwnerId> GetAsync(CancellationToken cancellationToken) => Task.FromResult(OwnerA);
    }

    private sealed class Counters(Dictionary<string, decimal> values) : IMaintenanceCounterSource
    {
        public bool Handles(MaintenanceSource source) => true;

        public Task<Result<MaintenanceReading>> ReadAsync(MaintenanceSource source, CancellationToken cancellationToken) =>
            Task.FromResult<Result<MaintenanceReading>>(new MaintenanceReading(values[source.Printer!], "h"));
    }

    private sealed class NoEvents : IEventSubscriber
    {
        public IDisposable Subscribe(Func<MarshalEvent, CancellationToken, ValueTask> handler) => throw new NotSupportedException();
    }
}
