using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Events;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Notifications;
using Marshal.Abstractions.Printers;
using Marshal.Abstractions.Suggestions;
using Marshal.Integrations.HomeAssistant.Suggestions;

namespace Marshal.Integrations.HomeAssistant.Tests.Suggestions;

public sealed class PrintDoneAwayDetectorTests
{
    private const string Power = "switch.voron_power";
    private static readonly OwnerId Owner = new(Guid.Parse("00000000-0000-7000-8000-00000000000a"));
    private static readonly DateTimeOffset At = new(2026, 10, 21, 14, 32, 0, TimeSpan.Zero);

    private readonly FixtureCache _cache = new();
    private readonly FixedPresence _presence = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CompleteWhileAway_Candidate_WithoutPowerEntity_NoAction()
    {
        _presence.State = PresenceState.Away;

        SuggestionCandidate candidate = Assert.Single(await Detector(power: null).EvaluateAsync(Context(), Ct));

        Assert.Equal("voron:202610211432", candidate.DedupeKey);
        Assert.Equal("voron", candidate.Facts["printer"]);
        Assert.Equal("benchy.gcode", candidate.Facts["file"]);
        Assert.Null(candidate.Action);
    }

    [Fact]
    public async Task WithPowerEntityOn_ProposesSwitchingItOff()
    {
        _presence.State = PresenceState.Away;
        WithPower("on");

        SuggestionCandidate candidate = Assert.Single(await Detector().EvaluateAsync(Context(), Ct));

        ProposedAction action = candidate.Action!;
        Assert.Equal("ha.HassTurnOff", action.Tool.ToString());
        Assert.Equal("Voron power", action.Arguments.GetProperty("name").GetString());
        Assert.Equal("switch", action.Arguments.GetProperty("domain")[0].GetString());
        Assert.Equal("Switch off Voron power", action.Summary);
        Assert.True(await Detector().StillHoldsAsync(Owner, candidate.DedupeKey, Ct));

        WithPower("off");
        Assert.False(await Detector().StillHoldsAsync(Owner, candidate.DedupeKey, Ct));
    }

    [Fact]
    public async Task PowerAlreadyOff_NoAction()
    {
        _presence.State = PresenceState.Away;
        WithPower("off");

        Assert.Null(Assert.Single(await Detector().EvaluateAsync(Context(), Ct)).Action);
    }

    [Theory]
    [InlineData(PresenceState.Home)]
    [InlineData(PresenceState.Unknown)]
    public async Task OwnerNotAway_Nothing(PresenceState state)
    {
        _presence.State = state;

        Assert.Empty(await Detector().EvaluateAsync(Context(), Ct));
    }

    [Fact]
    public async Task UnknownPrinterOrNoPrinters_Nothing()
    {
        _presence.State = PresenceState.Away;

        Assert.Empty(await Detector().EvaluateAsync(Context(printer: "mini"), Ct));
        Assert.Empty(await new PrintDoneAwayDetector(_cache, _presence, null).EvaluateAsync(Context(), Ct));
    }

    private static SuggestionContext Context(string printer = "voron") => new(
        Owner, "en", TimeZoneInfo.Utc,
        new MarshalEvent("printer", "complete", printer, JsonSerializer.SerializeToElement(new { printer, filename = "benchy.gcode", progress = 1.0 }), null, At));

    private PrintDoneAwayDetector Detector(string? power = Power) => new(_cache, _presence, new Directory(power));

    private void WithPower(string state)
    {
        HaSnapshot recorded = FixtureCache.Recorded;
        _cache.Current = recorded with
        {
            Entities = recorded.Entities.SetItem(Power, new HaEntity(Power, "Voron power", [], null, null, "outlet", true)),
            States = recorded.States.SetItem(Power, new HaState(Power, state, JsonSerializer.SerializeToElement(new { friendly_name = "Voron power" }), At)),
        };
    }

    private sealed class FixedPresence : IPresenceProvider
    {
        public PresenceState State { get; set; } = PresenceState.Unknown;

        public Task<Presence> GetAsync(OwnerId owner, CancellationToken cancellationToken) => Task.FromResult(new Presence(State));
    }

    private sealed class Directory(string? power) : IPrinterDirectory
    {
        public IReadOnlyList<PrinterInfo> Printers { get; } = [new PrinterInfo("voron", [], null, power)];

        public Result<PrinterInfo> Resolve(string? printer) =>
            Printers.FirstOrDefault(info => string.Equals(info.Name, printer, StringComparison.OrdinalIgnoreCase)) is { } found
                ? found
                : new Error("printer.not_found", "No such printer.", ErrorType.NotFound);
    }
}
