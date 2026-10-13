using System.Text.Json;
using Marshal.Abstractions.Runs;
using Marshal.Abstractions.Tools;
using Marshal.Core.Agent;

namespace Marshal.Core.Tests.Agent;

public sealed class DefaultToolCallSummarizerTests
{
    private static readonly ToolId _status = ToolId.Parse("printer.status").Value;

    private readonly DefaultToolCallSummarizer _summarizer = new(new GlobRedaction("mail.*"));

    [Fact]
    public void Arguments_KeyValuePairs_StringsCutTo60_TotalCutTo120()
    {
        Assert.Equal("printer=voron, layer=12, dry=true", _summarizer.Arguments(Json("""{"printer":"voron","layer":12,"dry":true}""")));
        Assert.Equal($"text={new string('a', 59)}…", _summarizer.Arguments(Json($$"""{"text":"{{new string('a', 80)}}"}""")));
        string many = _summarizer.Arguments(Json($$"""{"a":"{{new string('x', 50)}}","b":"{{new string('y', 50)}}","c":"{{new string('z', 50)}}"}"""));
        Assert.Equal(120, many.Length);
        Assert.EndsWith("…", many, StringComparison.Ordinal);
        Assert.Equal("", _summarizer.Arguments(Json("{}")));
    }

    [Fact]
    public void Arguments_NewlinesFlattened() =>
        Assert.Equal("text=line one line two", _summarizer.Arguments(Json("""{"text":"line one\nline two"}""")));

    [Fact]
    public void Result_FirstNonEmptyLineCutTo120()
    {
        Assert.Equal("Printing, layer 112.", _summarizer.Result(_status, false, "\nPrinting, layer 112.\nNozzle 240 °C.", null));
        Assert.Equal(new string('r', 119) + "…", _summarizer.Result(_status, false, new string('r', 200), null));
        Assert.Null(_summarizer.Result(_status, false, "  \n ", null));
    }

    [Fact]
    public void Result_ErrorCode() =>
        Assert.Equal("printer.offline", _summarizer.Result(_status, false, null, "printer.offline"));

    [Fact]
    public void Result_SensitiveOrRedacted_None()
    {
        Assert.Null(_summarizer.Result(ToolId.Parse("notes.read").Value, true, "Door code 1234.", null));
        Assert.Null(_summarizer.Result(ToolId.Parse("mail.search").Value, false, "Dear Lukas", null));
    }

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private sealed class GlobRedaction(string pattern) : IToolRedaction
    {
        public bool Redacts(ToolId tool, bool sensitive) => sensitive || ToolGlob.Matches(pattern, tool);
    }
}
