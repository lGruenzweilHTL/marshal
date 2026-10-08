using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using Marshal.Abstractions;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Tools;
using Marshal.Testing;
using Microsoft.Extensions.Options;
using static Marshal.Testing.TestExecutionContext;

namespace Marshal.Integrations.HomeAssistant.Tests;

public sealed class PlacesToolsTests
{
    private readonly FixtureCache _cache = new();
    private readonly FakeUserPreferencesStore _preferences = new();
    private readonly FakeCommands _commands = new();

    public PlacesToolsTests()
    {
        _preferences.Stored[OwnerA] = new UserPreferencesRecord("en", "Europe/Vienna", [], new NotificationTargets("person.alice", []), null);
        _cache.Current = Snapshot(
            Zone("zone.home", "Home", 100),
            Zone("zone.hornbach_linz", "Hornbach Linz", 200),
            Zone("zone.work", "Work", 150),
            Zone("zone.school_area", "School area", 300, passive: true),
            Person("person.alice", "not_home", 48.30, 14.29),
            Person("person.bob", "Hornbach Linz", 48.1, 14.1));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Find_MatchesNameAndIdCaseInsensitive()
    {
        JsonElement byName = await FindAsync(new { query = "HORNBACH" });
        JsonElement byId = await FindAsync(new { query = "hornbach_li" });

        JsonElement zone = Assert.Single(byName.GetProperty("zones").EnumerateArray());
        Assert.Equal(("zone.hornbach_linz", "Hornbach Linz", 200, "Hornbach Linz"),
            (zone.GetProperty("id").GetString(), zone.GetProperty("name").GetString(), zone.GetProperty("radius").GetInt32(), zone.GetProperty("personState").GetString()));
        Assert.Equal("zone.hornbach_linz", Assert.Single(byId.GetProperty("zones").EnumerateArray()).GetProperty("id").GetString());
        Assert.Equal("person.alice", byName.GetProperty("person").GetString());
    }

    [Fact]
    public async Task Find_HomeZone_PersonStateHome()
    {
        JsonElement zone = Assert.Single((await FindAsync(new { query = "home" })).GetProperty("zones").EnumerateArray());

        Assert.Equal(("zone.home", "home"), (zone.GetProperty("id").GetString(), zone.GetProperty("personState").GetString()));
    }

    [Fact]
    public async Task Find_PassiveZone_ListedWithoutPersonState()
    {
        JsonElement zone = Assert.Single((await FindAsync(new { query = "school" })).GetProperty("zones").EnumerateArray());

        Assert.True(zone.GetProperty("passive").GetBoolean());
        Assert.Equal(JsonValueKind.Null, zone.GetProperty("personState").ValueKind);
    }

    [Fact]
    public async Task Find_NoQuery_AllZonesWithOwnerInsideFromTheOwnersPersonOnly()
    {
        SetPerson("person.alice", "Work");

        JsonElement found = await FindAsync(new { });

        JsonElement[] zones = [.. found.GetProperty("zones").EnumerateArray()];
        Assert.Equal(4, zones.Length);
        Assert.Equal(["zone.work"], zones.Where(zone => zone.GetProperty("ownerInside").GetBoolean()).Select(zone => zone.GetProperty("id").GetString()));
    }

    [Fact]
    public async Task Find_NoPersonConfigured_NoPresenceAndNoPerson()
    {
        _preferences.Stored.Remove(OwnerA);

        JsonElement found = await FindAsync(new { query = "work" });

        Assert.Equal(JsonValueKind.Null, found.GetProperty("person").ValueKind);
        Assert.Equal(JsonValueKind.Null, Assert.Single(found.GetProperty("zones").EnumerateArray()).GetProperty("ownerInside").ValueKind);
    }

    [Fact]
    public async Task Find_CacheNotLoaded_Unavailable()
    {
        _cache.Current = null;

        Result<ToolOutput> found = await InvokeAsync("find", new { });

        Assert.Equal("ha.unavailable", found.Error.Code);
    }

    [Fact]
    public async Task Tools_TiersAndHiddenWithoutHomeAssistant()
    {
        IReadOnlyList<ITool> tools = await ToolsAsync();

        Assert.Equal(
            [("places.create_here", RiskTier.Confirm), ("places.find", RiskTier.Safe)],
            tools.Select(tool => (tool.Descriptor.Id.ToString(), tool.Descriptor.DefaultTier)).OrderBy(tool => tool.Item1, StringComparer.Ordinal));
        Assert.All(tools, tool => Assert.False(tool.Descriptor.UntrustedOutput));
        Assert.Empty(await new PlacesToolSource(_cache, _commands, _preferences, Options.Create(new HomeAssistantOptions())).GetToolsAsync(Ct));
    }

    [Fact]
    public async Task CreateHere_HiddenWithoutPersonEntity()
    {
        Assert.True(await (await ToolAsync("create_here")).IsAvailableAsync(Text(OwnerA), Ct));
        Assert.True(await (await ToolAsync("find")).IsAvailableAsync(Text(OwnerB), Ct));

        Assert.False(await (await ToolAsync("create_here")).IsAvailableAsync(Text(OwnerB), Ct));
    }

    [Theory]
    [InlineData(49)]
    [InlineData(1001)]
    public async Task CreateHere_RadiusOutOfRange_ValidationError(int radius)
    {
        ITool tool = await ToolAsync("create_here");

        Result valid = tool.Validate(JsonSerializer.SerializeToElement(new { name = "Baumarkt", radius }));

        Assert.Equal(ErrorType.Validation, valid.Error.Type);
        Assert.Equal(ErrorType.Validation, (await InvokeAsync("create_here", new { name = "Baumarkt", radius })).Error.Type);
        Assert.Empty(_commands.Sent);
    }

    [Fact]
    public async Task CreateHere_EmptyName_ValidationError()
    {
        Assert.Equal(ErrorType.Validation, (await ToolAsync("create_here")).Validate(JsonSerializer.SerializeToElement(new { name = " " })).Error.Type);
    }

    [Fact]
    public async Task CreateHere_NoGps_ErrorWithoutCommand()
    {
        SetPerson("person.alice", "not_home");

        Result<ToolOutput> created = await InvokeAsync("create_here", new { name = "Baumarkt" });

        Assert.Contains("position", created.Error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(_commands.Sent);
    }

    [Fact]
    public async Task CreateHere_DuplicateName_ErrorNamingTheZoneWithoutCommand()
    {
        Result<ToolOutput> created = await InvokeAsync("create_here", new { name = "hornbach linz" });

        Assert.Contains("zone.hornbach_linz", created.Error.Message, StringComparison.Ordinal);
        Assert.Empty(_commands.Sent);
    }

    [Fact]
    public async Task CreateHere_SendsZoneCreateAtThePersonsPositionAndReturnsTheZone()
    {
        Result<ToolOutput> created = await InvokeAsync("create_here", new { name = "Baumarkt" });

        (string type, JsonObject fields) = Assert.Single(_commands.Sent);
        Assert.Equal("zone/create", type);
        Assert.Equal(("Baumarkt", 48.30, 14.29, 150), ((string?)fields["name"], (double?)fields["latitude"], (double?)fields["longitude"], (int?)fields["radius"]));
        using var output = JsonDocument.Parse(created.Value.Content);
        JsonElement zone = output.RootElement.GetProperty("zone");
        Assert.Equal(("zone.baumarkt", "Baumarkt", 150, "Baumarkt", false),
            (zone.GetProperty("id").GetString(), zone.GetProperty("name").GetString(), zone.GetProperty("radius").GetInt32(), zone.GetProperty("personState").GetString(), zone.GetProperty("passive").GetBoolean()));
        Assert.Equal((48.30, 14.29), (output.RootElement.GetProperty("latitude").GetDouble(), output.RootElement.GetProperty("longitude").GetDouble()));
    }

    [Fact]
    public async Task CreateHere_HomeAssistantRejects_CommandFailedWithItsMessage()
    {
        _commands.Failure = new Error("ha.command_failed", "Unauthorized", ErrorType.Unavailable);

        Result<ToolOutput> created = await InvokeAsync("create_here", new { name = "Baumarkt", radius = 300 });

        Assert.Equal(("ha.command_failed", "Unauthorized"), (created.Error.Code, created.Error.Message));
    }

    [Fact]
    public async Task CreateHere_SummaryNamesZoneAndRadius()
    {
        string summary = (await ToolAsync("create_here")).Summarize(JsonSerializer.SerializeToElement(new { name = "Baumarkt", radius = 300 }), "en");

        Assert.Contains("Baumarkt", summary, StringComparison.Ordinal);
        Assert.Contains("300 m", summary, StringComparison.Ordinal);
        Assert.Contains("current position", summary, StringComparison.Ordinal);
    }

    private async Task<JsonElement> FindAsync(object arguments)
    {
        Result<ToolOutput> found = await InvokeAsync("find", arguments);
        return JsonDocument.Parse(found.Value.Content).RootElement.Clone();
    }

    private async Task<Result<ToolOutput>> InvokeAsync(string name, object arguments) =>
        await (await ToolAsync(name)).InvokeAsync(new ToolInvocation(JsonSerializer.SerializeToElement(arguments), Text(OwnerA)), Ct);

    private async Task<ITool> ToolAsync(string name) => (await ToolsAsync()).Single(tool => tool.Descriptor.Id.Name == name);

    private Task<IReadOnlyList<ITool>> ToolsAsync() =>
        new PlacesToolSource(_cache, _commands, _preferences, Options.Create(new HomeAssistantOptions { Url = new Uri("http://ha.local/") })).GetToolsAsync(Ct);

    private void SetPerson(string entity, string state) =>
        _cache.Current = _cache.Current! with { States = _cache.Current.States.SetItem(entity, new HaState(entity, state, JsonDocument.Parse("""{"friendly_name":"Alice"}""").RootElement, DateTimeOffset.UnixEpoch)) };

    private static HaState Zone(string entity, string name, int radius, bool passive = false) =>
        new(entity, "0", JsonSerializer.SerializeToElement(new { latitude = 48.0, longitude = 14.0, radius, passive, persons = Array.Empty<string>(), friendly_name = name }), DateTimeOffset.UnixEpoch);

    private static HaState Person(string entity, string state, double latitude, double longitude) =>
        new(entity, state, JsonSerializer.SerializeToElement(new { latitude, longitude, gps_accuracy = 10, friendly_name = entity }), DateTimeOffset.UnixEpoch);

    private static HaSnapshot Snapshot(params HaState[] states) => new()
    {
        Areas = ImmutableDictionary<string, HaArea>.Empty,
        Floors = ImmutableDictionary<string, HaFloor>.Empty,
        Devices = ImmutableDictionary<string, HaDevice>.Empty,
        Entities = ImmutableDictionary<string, HaEntity>.Empty,
        States = states.ToImmutableDictionary(state => state.EntityId),
    };

    private sealed class FakeCommands : IHomeAssistantCommands
    {
        public List<(string Type, JsonObject Fields)> Sent { get; } = [];

        public Error? Failure { get; set; }

        public Task<Result<JsonElement>> CommandAsync(string type, JsonObject fields, CancellationToken cancellationToken)
        {
            Sent.Add((type, (JsonObject)fields.DeepClone()));
            if (Failure is { } failure)
                return Task.FromResult<Result<JsonElement>>(failure);

            // HA answers with the stored zone; storage ids are the slugified name.
            var zone = new JsonObject
            {
                ["id"] = ((string)fields["name"]!).ToLowerInvariant().Replace(' ', '_'),
                ["name"] = fields["name"]!.DeepClone(),
                ["latitude"] = fields["latitude"]!.DeepClone(),
                ["longitude"] = fields["longitude"]!.DeepClone(),
                ["radius"] = fields["radius"]!.DeepClone(),
                ["passive"] = false,
            };
            return Task.FromResult<Result<JsonElement>>(JsonSerializer.SerializeToElement(zone));
        }
    }
}
