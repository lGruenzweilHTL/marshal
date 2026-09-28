using Marshal.Abstractions;
using Marshal.Abstractions.Guard;
using Marshal.Abstractions.Models;
using Marshal.Core.Guard;
using Marshal.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Marshal.Core.Tests.Guard;

public sealed class LlmConfirmationJudgeTests
{
    private readonly FakeChatClient _client = new();
    private readonly FakeTimeProvider _time = new();

    [Theory]
    [InlineData("""{"verdict":"positive"}""", JudgeVerdict.Positive)]
    [InlineData("""{"verdict":"neutral"}""", JudgeVerdict.Neutral)]
    [InlineData("""{"verdict":"negative"}""", JudgeVerdict.Negative)]
    [InlineData(" positive \n", JudgeVerdict.Positive)]
    [InlineData("NEGATIVE", JudgeVerdict.Negative)]
    public async Task Judge_ParsesVerdict(string reply, JudgeVerdict expected)
    {
        _client.ThenText(reply);

        JudgeVerdict verdict = await Judge().JudgeAsync("unlock the front door", "yeah go ahead", "en", Ct);

        Assert.Equal(expected, verdict);
    }

    [Theory]
    [InlineData("Sure, positive!")]
    [InlineData("""{"verdict":"maybe"}""")]
    [InlineData("""{"other":"positive"}""")]
    [InlineData("")]
    public async Task Judge_UnparsableOutput_Fails(string reply)
    {
        _client.ThenText(reply);

        Assert.Equal(JudgeVerdict.Failed, await Judge().JudgeAsync("x", "y", "en", Ct));
    }

    [Fact]
    public async Task Judge_SendsOnlySummaryAndReplyAtTemperatureZeroWithSchema()
    {
        _client.ThenText("""{"verdict":"neutral"}""");

        await Judge().JudgeAsync("unlock the front door", "what time is it?", "en", Ct);

        FakeChatRequest request = Assert.Single(_client.Requests);
        Assert.Equal(0f, request.Options?.Temperature);
        Assert.Empty(request.ToolNames);
        Assert.IsType<ChatResponseFormatJson>(request.Options?.ResponseFormat);
        Assert.Contains("neutral", request.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains("not confident", request.SystemPrompt, StringComparison.Ordinal);
        string user = Assert.Single(request.Messages, message => message.Role == ChatRole.User).Text;
        Assert.Contains("unlock the front door", user, StringComparison.Ordinal);
        Assert.Contains("what time is it?", user, StringComparison.Ordinal);
        Assert.Equal(2, request.Messages.Count);
    }

    [Fact]
    public async Task Judge_Timeout_Fails()
    {
        _client.ThenHang();

        Task<JudgeVerdict> judging = Judge().JudgeAsync("x", "y", "en", Ct);
        _time.Advance(TimeSpan.FromSeconds(3));

        Assert.Equal(JudgeVerdict.Failed, await judging);
    }

    [Fact]
    public async Task Judge_ClientThrows_Fails()
    {
        _client.ThenThrow(new HttpRequestException("down"));

        Assert.Equal(JudgeVerdict.Failed, await Judge().JudgeAsync("x", "y", "en", Ct));
    }

    [Fact]
    public async Task Judge_NoFastModel_Fails()
    {
        var judge = new LlmConfirmationJudge(new NoModelRouter(), Options.Create(new ConfirmationOptions()), _time, NullLogger<LlmConfirmationJudge>.Instance);

        Assert.Equal(JudgeVerdict.Failed, await judge.JudgeAsync("x", "y", "en", Ct));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private LlmConfirmationJudge Judge() =>
        new(new SingleModelRouter(_client), Options.Create(new ConfirmationOptions()), _time, NullLogger<LlmConfirmationJudge>.Instance);

    private sealed class NoModelRouter : IModelRouter
    {
        public Result<RoutedModel> Route(ModelRequest request) => new Error("model.not_available", "none");
    }
}
