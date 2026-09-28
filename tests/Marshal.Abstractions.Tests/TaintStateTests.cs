using Marshal.Abstractions.Execution;

namespace Marshal.Abstractions.Tests;

public sealed class TaintStateTests
{
    [Fact]
    public void New_IsClean()
    {
        var taint = new TaintState();

        Assert.False(taint.IsTainted);
        Assert.Empty(taint.Sources);
    }

    [Fact]
    public void Mark_RecordsKindAndFirstStep()
    {
        var taint = new TaintState();

        taint.Mark("mail", 3);
        taint.Mark("mail", 7);
        taint.Mark("rss", 9);

        Assert.True(taint.IsTainted);
        Assert.Equal([new TaintSource("mail", 3), new TaintSource("rss", 9)], taint.Sources);
    }

    [Fact]
    public void Counters_IncrementPerKey()
    {
        var counters = new RunCounters();

        counters.Increment("agent.ask_advisor");
        counters.Increment("agent.ask_advisor");

        Assert.Equal(2, counters.Get("agent.ask_advisor"));
        Assert.Equal(0, counters.Get("other"));
    }
}
