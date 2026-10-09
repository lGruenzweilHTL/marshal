using Marshal.Core.Displays;
using Microsoft.Extensions.Options;

namespace Marshal.Core.Tests.Displays;

public sealed class DisplayOptionsValidatorTests
{
    [Fact]
    public void Valid_Succeeds()
    {
        ValidateOptionsResult result = Validate(Options(("living_tv", "media_player.living_room_tv")));

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Defaults_Succeed()
    {
        Assert.True(Validate(new DisplayOptions()).Succeeded);
    }

    [Theory]
    [InlineData("Living", "displays.surfaces.Living must be named")]
    [InlineData("1tv", "displays.surfaces.1tv must be named")]
    public void InvalidName_Fails(string name, string expected)
    {
        ValidateOptionsResult result = Validate(Options((name, "media_player.tv")));

        Assert.Contains(result.Failures!, failure => failure.StartsWith(expected, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("light.living_room")]
    [InlineData("media_player.")]
    [InlineData("")]
    public void WrongEntityDomain_Fails(string entity)
    {
        ValidateOptionsResult result = Validate(Options(("living_tv", entity)));

        Assert.Contains(result.Failures!, failure => failure.StartsWith("displays.surfaces.living_tv.entity ", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(1441)]
    public void LinkMinutesOutOfRange_Fails(int minutes)
    {
        DisplayOptions options = Options();
        options.LinkMinutes = minutes;

        Assert.Contains(Validate(options).Failures!, failure => failure.StartsWith("displays.linkMinutes ", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("ftp://nas.lan/")]
    [InlineData("/relative/")]
    public void BaseUrlNotHttp_Fails(string url)
    {
        DisplayOptions options = Options();
        options.MediaBaseUrl = new Uri(url, UriKind.RelativeOrAbsolute);

        Assert.Contains(Validate(options).Failures!, failure => failure.StartsWith("displays.mediaBaseUrl ", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("http://192.168.1.10:8080/")]
    [InlineData("https://marshal.example.com/")]
    public void BaseUrlHttp_Succeeds(string url)
    {
        DisplayOptions options = Options();
        options.MediaBaseUrl = new Uri(url);

        Assert.True(Validate(options).Succeeded);
    }

    internal static DisplayOptions Options(params (string Name, string Entity)[] surfaces)
    {
        var options = new DisplayOptions();
        foreach ((string name, string entity) in surfaces)
            options.Surfaces[name] = new DisplaySurfaceOptions { Entity = entity };
        return options;
    }

    private static ValidateOptionsResult Validate(DisplayOptions options) => new DisplayOptionsValidator().Validate(null, options);
}
