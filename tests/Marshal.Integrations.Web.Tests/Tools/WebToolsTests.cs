using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Extraction;
using Marshal.Abstractions.Sessions;
using Marshal.Abstractions.Tools;
using Marshal.Integrations.Web.Tools;
using Marshal.Testing;
using Microsoft.Extensions.Time.Testing;

namespace Marshal.Integrations.Web.Tests.Tools;

public sealed class WebToolsTests
{
    private readonly FakeWebSearch _search = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 18, 9, 0, 0, TimeSpan.Zero));
    private readonly SessionWebResults _results;
    private readonly MarshalExecutionContext _context = TestExecutionContext.Text(language: "de") with { SessionId = new SessionId(Guid.NewGuid()) };

    public WebToolsTests() => _results = new SessionWebResults(_time);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Tools_SearchIsUntrustedWebTaint_AnswerIsNot_BothSafeAndNotPersisting()
    {
        IReadOnlyList<ITool> tools = await ToolsAsync(ScriptedExtractor.Answering(true));

        Assert.Equal(["web.answer", "web.search"], tools.Select(tool => tool.Descriptor.Id.ToString()).Order(StringComparer.Ordinal));
        ToolDescriptor search = tools.Single(tool => tool.Descriptor.Id.Name == "search").Descriptor;
        Assert.True(search.UntrustedOutput);
        Assert.Equal("web", search.TaintKind);
        Assert.False(tools.Single(tool => tool.Descriptor.Id.Name == "answer").Descriptor.UntrustedOutput);
        // Safe and not persisting: taint never escalates a query to confirm tier.
        Assert.All(tools, tool => Assert.Equal((RiskTier.Safe, false), (tool.Descriptor.DefaultTier, tool.Descriptor.Persists)));
    }

    [Fact]
    public async Task Hidden_WhenNotConfigured()
    {
        _search.IsConfigured = false;

        foreach (ITool tool in await ToolsAsync(ScriptedExtractor.Answering(true)))
            Assert.False(await tool.IsAvailableAsync(_context, Ct));
    }

    [Fact]
    public async Task Search_ReturnsResultsWithIdsAndKeepsThemForTheSession()
    {
        ITool search = await ToolAsync("search", ScriptedExtractor.Answering(true));

        Result<ToolOutput> output = await search.InvokeAsync(new ToolInvocation(Args(new { query = "christmas market linz", limit = 1 }), _context), Ct);

        Assert.Equal([("christmas market linz", "de", 1)], _search.Searches);
        JsonElement hit = Assert.Single(JsonDocument.Parse(output.Value.Content).RootElement.EnumerateArray());
        Assert.Equal(("Christmas market Linz", "https://www.linztourismus.at/maerkte", "linztourismus.at"), (hit.GetProperty("title").GetString(), hit.GetProperty("url").GetString(), hit.GetProperty("domain").GetString()));
        string id = hit.GetProperty("id").GetString()!;
        Assert.Equal($"web:{id}", output.Value.Reference);
        Assert.Equal("Christmas market Linz", _results.Find(_context, id)?.Title);

        _time.Advance(TimeSpan.FromMinutes(61));
        Assert.Null(_results.Find(_context, id));
    }

    [Fact]
    public async Task Search_LimitDefaultsToAndIsCappedByMaxResults()
    {
        _search.MaxResults = 5;
        ITool search = await ToolAsync("search", ScriptedExtractor.Answering(true));

        await search.InvokeAsync(new ToolInvocation(Args(new { query = "a" }), _context), Ct);
        await search.InvokeAsync(new ToolInvocation(Args(new { query = "b", limit = 20 }), _context), Ct);

        Assert.Equal([5, 5], _search.Searches.Select(call => call.Limit));
    }

    [Fact]
    public async Task Search_QueryOver300CharactersFailsValidation()
    {
        ITool search = await ToolAsync("search", ScriptedExtractor.Answering(true));

        Assert.True(search.Validate(Args(new { query = new string('a', 301) })).IsFailure);
        Assert.True(search.Validate(Args(new { query = new string('a', 300) })).IsSuccess);
    }

    [Fact]
    public async Task Search_ServiceDown_Unavailable()
    {
        _search.Failure = new Error("web.unavailable", "The search service cannot be reached.", ErrorType.Unavailable);
        ITool search = await ToolAsync("search", ScriptedExtractor.Answering(true));

        Assert.Equal("web.unavailable", (await search.InvokeAsync(new ToolInvocation(Args(new { query = "x" }), _context), Ct)).Error.Code);
    }

    [Fact]
    public async Task Answer_OnlyTitlesAndSnippetsReachTheExtractor_TypedAnswerDoesNotTaint()
    {
        var extractor = ScriptedExtractor.Answering("2026-11-20");
        ITool answer = await ToolAsync("answer", extractor);

        Result<ToolOutput> output = await answer.InvokeAsync(new ToolInvocation(Args(new { question = "When does the Linz Christmas market open?", answerType = "date" }), _context), Ct);

        (UntrustedItem item, string question, AnswerSpec spec) = Assert.Single(extractor.Calls);
        Assert.Equal(("When does the Linz Christmas market open?", AnswerType.Date, "web"), (question, spec.Type, item.TaintKind));
        Assert.Contains("Title: Christmas market Linz", item.Text, StringComparison.Ordinal);
        Assert.Contains("Snippet: Seit 1990 am Hauptplatz.", item.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("https://", item.Text, StringComparison.Ordinal);
        Assert.Equal("""{"answer":"2026-11-20","domains":["linztourismus.at","de.wikipedia.org"]}""", output.Value.Content);
        Assert.Null(output.Value.TaintKind);
    }

    [Fact]
    public async Task Answer_TextTaintsWithWeb()
    {
        ITool answer = await ToolAsync("answer", ScriptedExtractor.Answering("It opens in November.", taints: true));

        Result<ToolOutput> output = await answer.InvokeAsync(new ToolInvocation(Args(new { question = "What is it?", answerType = "text" }), _context), Ct);

        Assert.Equal("web", output.Value.TaintKind);
    }

    [Fact]
    public async Task Answer_UnknownPassesThrough()
    {
        ITool answer = await ToolAsync("answer", ScriptedExtractor.Answering("unknown", unknown: true));

        Result<ToolOutput> output = await answer.InvokeAsync(new ToolInvocation(Args(new { question = "Who won?", answerType = "enum", values = new[] { "home", "away" } }), _context), Ct);

        Assert.Equal("unknown", JsonDocument.Parse(output.Value.Content).RootElement.GetProperty("answer").GetString());
        Assert.Null(output.Value.TaintKind);
    }

    [Theory]
    [InlineData("""{"question":"x","answerType":"maybe"}""")]
    [InlineData("""{"question":"x","answerType":"1"}""")]
    [InlineData("""{"question":"x","answerType":"enum"}""")]
    [InlineData("""{"answerType":"date"}""")]
    public async Task Answer_InvalidArgumentsFailValidation(string arguments)
    {
        ITool answer = await ToolAsync("answer", ScriptedExtractor.Answering(true));

        Assert.True(answer.Validate(JsonDocument.Parse(arguments).RootElement).IsFailure);
    }

    private async Task<IReadOnlyList<ITool>> ToolsAsync(IQuarantinedExtractor extractor) =>
        await new WebToolSource(_search, _results, extractor).GetToolsAsync(Ct);

    private async Task<ITool> ToolAsync(string name, IQuarantinedExtractor extractor) =>
        (await ToolsAsync(extractor)).Single(tool => tool.Descriptor.Id.Name == name);

    private static JsonElement Args(object value) => JsonSerializer.SerializeToElement(value);
}
