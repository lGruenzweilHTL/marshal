using Marshal.Core.Music;
using Microsoft.Extensions.Options;

namespace Marshal.Core.Tests.Music;

public sealed class MusicOptionsValidatorTests
{
    [Fact]
    public void Valid_Succeeds()
    {
        Assert.True(Validate(Options(("kitchen", "media_player.kitchen_ma"), ("living_room", "media_player.sonos"))).Succeeded);
    }

    [Fact]
    public void Defaults_Succeed()
    {
        Assert.True(Validate(new MusicOptions()).Succeeded);
    }

    [Theory]
    [InlineData("Kitchen")]
    [InlineData("1kitchen")]
    [InlineData("living-room")]
    public void InvalidName_FailsNamingKey(string name)
    {
        ValidateOptionsResult result = Validate(Options((name, "media_player.kitchen")));

        Assert.Contains(result.Failures!, failure => failure.StartsWith($"music.speakers.{name} must be named", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("light.kitchen")]
    [InlineData("media_player.")]
    [InlineData("")]
    public void WrongEntityDomain_FailsNamingKey(string entity)
    {
        ValidateOptionsResult result = Validate(Options(("kitchen", entity)));

        Assert.Contains(result.Failures!, failure => failure.StartsWith("music.speakers.kitchen must be a media_player entity id", StringComparison.Ordinal));
    }

    internal static MusicOptions Options(params (string Name, string Entity)[] speakers)
    {
        var options = new MusicOptions();
        foreach ((string name, string entity) in speakers)
            options.Speakers[name] = entity;
        return options;
    }

    private static ValidateOptionsResult Validate(MusicOptions options) => new MusicOptionsValidator().Validate(null, options);
}
