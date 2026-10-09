using Marshal.Abstractions.Context;
using Marshal.Abstractions.Satellites;
using Marshal.Core.Context;
using Marshal.Core.Satellites;
using Marshal.Testing;

namespace Marshal.Core.Tests.Satellites;

public sealed class YamlSatelliteContextProviderTests
{
    private const string Yaml = """
        satellites:
          assist_satellite.kitchen:
            verbosity: short
            notes: "Cooking area. Timer and recipe questions are common."
          assist_satellite.kids_room:
            verbosity: short
            denyConfirm: [printer.*, calendar.*]
            denyTools: [mail.*]
          assist_satellite.living_room:
            display: living_tv
        """;

    [Fact]
    public void GetProfile_Configured_ReturnsProfile()
    {
        YamlSatelliteContextProvider provider = Provider(Yaml);

        SatelliteProfile kids = provider.GetProfile("assist_satellite.kids_room");

        Assert.Equal("short", kids.Verbosity);
        Assert.Equal(["mail.*"], kids.DenyTools);
        Assert.Equal(["printer.*", "calendar.*"], kids.DenyConfirm);
    }

    [Theory]
    [InlineData("assist_satellite.unknown")]
    [InlineData(null)]
    public void GetProfile_UnknownOrNone_Defaults(string? satellite)
    {
        SatelliteProfile profile = Provider(Yaml).GetProfile(satellite);

        Assert.Equal(SatelliteProfile.Default, profile);
        Assert.Empty(profile.DenyTools);
    }

    [Fact]
    public async Task Prompt_ProfileNotesAppear()
    {
        var contributor = new SatelliteContextContributor(Provider(Yaml));

        string? text = await contributor.ContributeAsync(new ContextRequest(TestExecutionContext.Voice(satelliteId: "assist_satellite.kitchen"), "x"), Ct);

        Assert.Contains("Cooking area. Timer and recipe questions are common.", text, StringComparison.Ordinal);
        Assert.Contains("short", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Prompt_AreaNameInContext()
    {
        var contributor = new SatelliteContextContributor(Provider(Yaml));

        string? text = await contributor.ContributeAsync(new ContextRequest(TestExecutionContext.Voice(satelliteId: null, areaId: "living_room"), "x"), Ct);

        Assert.Contains("The user is speaking in the living room.", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Prompt_NoSatelliteNoArea_NothingAdded()
    {
        var contributor = new SatelliteContextContributor(Provider(Yaml));

        string? text = await contributor.ContributeAsync(new ContextRequest(TestExecutionContext.Text(), "x"), Ct);

        Assert.Null(text);
    }

    [Fact]
    public void GetProfile_Display_IsRead()
    {
        Assert.Equal("living_tv", Provider(Yaml).GetProfile("assist_satellite.living_room").Display);
        Assert.Null(Provider(Yaml).GetProfile("assist_satellite.kitchen").Display);
    }

    [Fact]
    public void Validate_KnownDisplay_Succeeds()
    {
        Assert.True(SatellitesFile.Validate(Parse(Yaml), ["living_tv"]).IsSuccess);
    }

    [Fact]
    public void Validate_UnknownDisplay_FailsNamingKey()
    {
        Abstractions.Result result = SatellitesFile.Validate(Parse(Yaml), ["office_tv"]);

        Assert.StartsWith("satellites.assist_satellite.living_room.display ", result.Error.Message, StringComparison.Ordinal);
        Assert.Contains("living_tv", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_UnknownVerbosity_Fails()
    {
        SatellitesFile file = Parse("satellites:\n  a.b:\n    verbosity: chatty\n");

        Assert.Contains("verbosity", SatellitesFile.Validate(file).Error.Message, StringComparison.Ordinal);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static YamlSatelliteContextProvider Provider(string yaml) => new(new StaticMonitor<SatellitesFile>(Parse(yaml)), new IdAreaDirectory());

    private static SatellitesFile Parse(string yaml) =>
        new YamlDotNet.Serialization.DeserializerBuilder()
            .WithNamingConvention(YamlDotNet.Serialization.NamingConventions.CamelCaseNamingConvention.Instance)
            .Build()
            .Deserialize<SatellitesFile>(yaml);
}
