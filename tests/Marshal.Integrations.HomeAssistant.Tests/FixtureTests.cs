namespace Marshal.Integrations.HomeAssistant.Tests;

public sealed class FixtureTests
{
    [Theory]
    [InlineData("websocket-session.json")]
    [InlineData("mcp-session.json")]
    [InlineData("rest-session.json")]
    public void Fixture_IsCopiedAndScrubbed(string file)
    {
        string text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", file));

        Assert.DoesNotContain("eyJ", text, StringComparison.Ordinal);
    }
}
