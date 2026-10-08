namespace Marshal.Integrations.HomeAssistant.Tests;

public sealed class FixtureTests
{
    [Theory]
    [InlineData("websocket-session.json")]
    [InlineData("mcp-session.json")]
    [InlineData("rest-session.json")]
    [InlineData("energy-prefs.json")]
    [InlineData("energy-prefs-legacy.json")]
    [InlineData("energy-prefs-empty.json")]
    [InlineData("energy-info.json")]
    [InlineData("energy-statistics.json")]
    public void Fixture_IsCopiedAndScrubbed(string file)
    {
        string text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", file));

        Assert.DoesNotContain("eyJ", text, StringComparison.Ordinal);
    }
}
