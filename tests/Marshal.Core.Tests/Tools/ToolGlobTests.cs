using Marshal.Abstractions.Tools;
using Marshal.Core.Tools;

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
