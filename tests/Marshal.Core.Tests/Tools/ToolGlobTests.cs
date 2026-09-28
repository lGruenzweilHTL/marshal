using Marshal.Abstractions.Tools;

namespace Marshal.Core.Tests.Tools;

public sealed class ToolGlobTests
{
    [Theory]
    [InlineData("mail.*", "mail.search", true)]
    [InlineData("mail.*", "mailx.search", false)]
    [InlineData("ha.Hass*", "ha.HassTurnOn", true)]
    [InlineData("ha.Hass*", "ha.GetLiveContext", false)]
    [InlineData("*", "agent.ask_advisor", true)]
    [InlineData("agent.ask_advisor", "agent.ask_advisor", true)]
    [InlineData("agent.ask_advisor", "agent.ask_advisorx", false)]
    [InlineData("printer.?ause", "printer.pause", true)]
    [InlineData("ha.hass*", "ha.HassTurnOn", false)]
    [InlineData("ha.a+b", "ha.a+b", true)]
    public void Matches_Glob(string pattern, string id, bool expected)
    {
        Assert.Equal(expected, ToolGlob.Matches(pattern, ToolId.Parse(id).Value));
    }
}

public sealed class ToolGlobEdgeTests
{
    [Theory]
    [InlineData("test.ab", "test.a", false)]
    [InlineData("test.a**", "test.a", true)]
    [InlineData("*.x", "test.y", false)]
    [InlineData("t*t.*", "test.do", true)]
    public void Matches_Edges(string pattern, string id, bool expected)
    {
        Assert.Equal(expected, ToolGlob.Matches(pattern, ToolId.Parse(id).Value));
    }
}
