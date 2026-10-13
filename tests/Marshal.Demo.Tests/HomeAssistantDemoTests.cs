using System.Text.Json;
using System.Text.Json.Nodes;
using Marshal.Abstractions;
using Marshal.Abstractions.Calendar;
using Marshal.Abstractions.Events;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Guard;
using Marshal.Abstractions.Music;
using Marshal.Abstractions.Tools;
using Marshal.Demo.HomeAssistant;
using Marshal.Integrations.HomeAssistant;
using Marshal.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Marshal.Demo.Tests;

public sealed class HomeAssistantDemoTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static MarshalExecutionContext Kitchen => TestExecutionContext.Voice() with { DeviceId = "kitchen-device", SatelliteId = "assist_satellite.kitchen" };

    [Fact]
    public async Task DemoOff_RealImplementationsStay()
    {
        await using var demo = DemoServices.Create(demo: false);

        Assert.IsType<HomeAssistantClient>(demo.Get<IHomeAssistantClient>());
        Assert.IsType<HomeAssistantCache>(demo.Get<IHomeAssistantCache>());
        Assert.Null(demo.Provider.GetService<DemoWorld>());
        Assert.DoesNotContain("ha.intent__HassTurnOn", (await demo.ToolsAsync()).Keys);
    }

    [Fact]
    public async Task HaConfigured_RealImplementationsStay()
    {
        await using var demo = DemoServices.Create(new() { ["ha:url"] = "http://homeassistant.local:8123", ["ha:tokenSecret"] = "ha_token" });

        Assert.IsType<HomeAssistantClient>(demo.Get<IHomeAssistantClient>());
        Assert.DoesNotContain(DemoIntegration.HomeAssistant, demo.Get<DemoIntegrations>().SimulatedAtStartup);
    }

    [Fact]
    public async Task HaNotConfigured_SimulatedAndToolsOffered()
    {
        await using var demo = DemoServices.Create();

        Assert.IsType<DemoHomeAssistantClient>(demo.Get<IHomeAssistantClient>());
        Assert.IsType<DemoHomeAssistantCache>(demo.Get<IHomeAssistantCache>());
        Assert.IsType<DemoHomeAssistantCommands>(demo.Get<IHomeAssistantCommands>());
        Assert.IsType<DemoCalendarProvider>(demo.Get<ICalendarProvider>());
        Assert.Contains(DemoIntegration.HomeAssistant, demo.Get<DemoIntegrations>().SimulatedAtStartup);
        Assert.Contains(demo.Provider.GetServices<IEventSource>(), source => source is DemoHomeAssistantEventSource { Source: "ha", IsConnected: true });
        Assert.DoesNotContain(demo.Provider.GetServices<IEventSource>(), source => source is HomeAssistantEventSource);
        Assert.DoesNotContain(demo.Provider.GetServices<IHostedService>(), service => service is HomeAssistantCache);
        Dictionary<string, ITool> tools = await demo.ToolsAsync();
        foreach (string id in new[] { "timers.start", "places.find", "energy.usage", "calendar.list_events", "music.play", "ha.intent__HassTurnOn", "ha.homeassistant__GetLiveContext" })
            Assert.Contains(id, tools.Keys);
    }

    [Fact]
    public async Task ServiceCall_ChangesStateAndPublishesStateChanged()
    {
        await using var demo = DemoServices.Create();
        _ = demo.Get<DemoHomeAssistantEventSource>();

        Result<JsonElement> called = await demo.Get<IHomeAssistantClient>().CallServiceAsync("light", "turn_on", new JsonObject { ["entity_id"] = "light.kitchen", ["brightness_pct"] = 50 }, Ct);

        Assert.True(called.IsSuccess);
        HaState state = demo.Get<IHomeAssistantCache>().Current!.States["light.kitchen"];
        Assert.Equal(("on", 128), (state.State, state.Attributes.GetProperty("brightness").GetInt32()));
        MarshalEvent published = Assert.Single(demo.Events.Events);
        Assert.Equal(("ha", "state_changed", "light.kitchen"), (published.Source, published.Type, published.Entity));
        Assert.Equal(("off", "on"), (published.Payload.GetProperty("from").GetString(), published.Payload.GetProperty("to").GetString()));
    }

    [Fact]
    public async Task UnknownService_ServiceNotSupported()
    {
        await using var demo = DemoServices.Create();

        Result<JsonElement> called = await demo.Get<IHomeAssistantClient>().CallServiceAsync("vacuum", "start", new JsonObject { ["entity_id"] = "light.kitchen" }, Ct);

        Assert.Equal("ha.service_not_supported", called.Error.Code);
    }

    [Fact]
    public async Task Timers_StartStatusCancelOnTheTurnDevice()
    {
        await using var demo = DemoServices.Create();
        Dictionary<string, ITool> tools = await demo.ToolsAsync();

        Assert.Equal("Pasta timer started for 10 minutes.", await DemoServices.InvokeAsync(tools["timers.start"], """{"duration":"10m","name":"pasta"}""", Kitchen));
        demo.Time.Advance(TimeSpan.FromMinutes(3));
        Assert.Equal("Pasta: 7 minutes left.", await DemoServices.InvokeAsync(tools["timers.status"], "{}", Kitchen));
        Assert.Equal("Timer cancelled.", await DemoServices.InvokeAsync(tools["timers.cancel"], "{}", Kitchen));
        Assert.Equal("No timers are running.", await DemoServices.InvokeAsync(tools["timers.status"], "{}", Kitchen));
    }

    [Fact]
    public async Task Energy_UsageFromSimulatedStatistics()
    {
        await using var demo = DemoServices.Create();
        ITool energy = (await demo.ToolsAsync())["energy.usage"];

        Assert.True(await energy.IsAvailableAsync(TestExecutionContext.Text(), Ct));
        JsonElement usage = JsonDocument.Parse(await DemoServices.InvokeAsync(energy, """{"period":"yesterday"}""")).RootElement;

        Assert.True(usage.GetProperty("gridImport").GetDouble() > 0);
        Assert.True(usage.GetProperty("solar").GetDouble() > 0);
    }

    [Fact]
    public async Task Calendar_ListsAndCreatesEvents()
    {
        await using var demo = DemoServices.Create();
        Dictionary<string, ITool> tools = await demo.ToolsAsync();

        string today = await DemoServices.InvokeAsync(tools["calendar.list_events"], $$"""{"calendar":"calendar.family","start":"{{Today(demo)}}","end":"{{Today(demo)}}"}""");
        Assert.Contains("Dentist (Sam)", today, StringComparison.Ordinal);

        Result created = await demo.Get<ICalendarProvider>().CreateEventAsync("calendar.family", new CalendarEventData("Pick up filament", CalendarTime.At(demo.Time.GetUtcNow().AddHours(2)), CalendarTime.At(demo.Time.GetUtcNow().AddHours(3))), Ct);
        Assert.True(created.IsSuccess);
        Assert.Contains("Pick up filament", await DemoServices.InvokeAsync(tools["calendar.list_events"], $$"""{"calendar":"calendar.family","start":"{{Today(demo)}}","end":"{{Today(demo)}}"}"""), StringComparison.Ordinal);
    }

    private static string Today(DemoServices demo) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(demo.Time.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById("Europe/Vienna")).DateTime).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public async Task Music_PlayOnTheSatelliteSpeakerShowsOnItsState()
    {
        await using var demo = DemoServices.Create();
        Dictionary<string, ITool> tools = await demo.ToolsAsync();

        await DemoServices.InvokeAsync(tools["music.play"], """{"item":"Kind of Blue","type":"album"}""", Kitchen);

        HaState speaker = demo.World.Home.Snapshot.States["media_player.kitchen"];
        Assert.Equal(("playing", "Kind of Blue"), (speaker.State, speaker.Attributes.GetProperty("media_title").GetString()));
        Assert.Contains("Kind of Blue", await DemoServices.InvokeAsync(tools["music.queue"], "{}", Kitchen), StringComparison.Ordinal);
        Assert.Equal(["kitchen", "living_room", "office"], demo.Get<ISpeakerDirectory>().Names);
    }

    [Fact]
    public async Task HaTools_HiddenWhenAnMcpServerHaIsConfigured()
    {
        await using var demo = DemoServices.Create();
        demo.Mcp.Definitions.Add(FakeMcpServerCatalog.Server("ha"));

        Assert.Empty(await demo.Get<IEnumerable<Marshal.Abstractions.Tools.IToolSource>>().OfType<DemoHaToolSource>().Single().GetToolsAsync(Ct));
    }

    [Fact]
    public async Task HaTools_SameIdsConfirmByDefault()
    {
        await using var demo = DemoServices.Create();

        IReadOnlyList<ITool> tools = await demo.Provider.GetServices<IToolSource>().OfType<DemoHaToolSource>().Single().GetToolsAsync(Ct);

        Assert.Equal(["ha.intent__HassTurnOn", "ha.intent__HassTurnOff", "ha.intent__HassLightSet", "ha.intent__HassClimateSetTemperature", "ha.homeassistant__GetLiveContext"], tools.Select(tool => tool.Descriptor.Id.ToString()));
        Assert.All(tools, tool => Assert.Equal(RiskTier.Confirm, tool.Descriptor.DefaultTier));
    }

    [Fact]
    public async Task HassTurnOn_ByNameAndByArea_ThenLiveContextShowsIt()
    {
        await using var demo = DemoServices.Create();
        Dictionary<string, ITool> tools = await demo.ToolsAsync();

        string byName = await DemoServices.InvokeAsync(tools["ha.intent__HassTurnOn"], """{"name":"Kitchen light"}""");
        await DemoServices.InvokeAsync(tools["ha.intent__HassTurnOn"], """{"area":"Office","domain":["light"]}""");
        await DemoServices.InvokeAsync(tools["ha.intent__HassLightSet"], """{"area":"Living room","brightness":20}""");

        Assert.Contains("action_done", byName, StringComparison.Ordinal);
        Assert.Equal("on", demo.World.Home.Snapshot.States["light.kitchen"].State);
        Assert.Equal("on", demo.World.Home.Snapshot.States["light.office"].State);
        Assert.Equal(51, demo.World.Home.Snapshot.States["light.living_room"].Attributes.GetProperty("brightness").GetInt32());
        string context = await DemoServices.InvokeAsync(tools["ha.homeassistant__GetLiveContext"], "{}");
        Assert.Contains("- names: Kitchen light\n  domain: light\n  state: 'on'", context, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HassTurnOn_NoMatch_ErrorResponse()
    {
        await using var demo = DemoServices.Create();

        string response = await DemoServices.InvokeAsync((await demo.ToolsAsync())["ha.intent__HassTurnOn"], """{"area":"Attic"}""");

        Assert.Contains("\"response_type\":\"error\"", response, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TargetResolver_ResolvesAgainstTheDemoHome()
    {
        await using var demo = DemoServices.Create();
        IRiskTargetResolver resolver = demo.Provider.GetServices<IRiskTargetResolver>().Single(candidate => candidate.Namespace == "ha");

        Result<IReadOnlyList<RiskTarget>> targets = await resolver.ResolveAsync(new ToolId("ha", "intent__HassTurnOff"), JsonDocument.Parse("""{"name":"Front door lock"}""").RootElement, Ct);

        RiskTarget target = Assert.Single(targets.Value);
        Assert.Equal("lock", target.Attributes["domain"]);
    }
}
