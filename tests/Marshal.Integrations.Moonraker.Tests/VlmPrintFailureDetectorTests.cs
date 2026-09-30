using Marshal.Abstractions;
using Marshal.Abstractions.Models;
using Marshal.Abstractions.Printers;
using Marshal.Testing;
using Microsoft.Extensions.AI;

namespace Marshal.Integrations.Moonraker.Tests;

public sealed class VlmPrintFailureDetectorTests
{
    private static readonly PrinterSnapshot _snapshot = new("voron", "s1", "/s1", [0xFF, 0xD8, 0xFF, 0xE0], 1024, 768, DateTimeOffset.UnixEpoch);

    private readonly FakeVisionRouter _router = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Detect_FastVisionModelGetsTheImage_ParsesTheVerdict()
    {
        FakeChatClient fast = new FakeChatClient().ThenText("""{"failure": true, "reason": "spaghetti next to the part"}""");
        _router.Vision[ModelTier.Fast] = fast;

        Result<PrintVerdict> verdict = await new VlmPrintFailureDetector(_router).DetectAsync(_snapshot, Ct);

        Assert.Equal(new PrintVerdict(true, "spaghetti next to the part"), verdict.Value);
        DataContent image = Assert.Single(fast.Requests[0].Messages[1].Contents.OfType<DataContent>());
        Assert.Equal("image/jpeg", image.MediaType);
        Assert.Equal(_snapshot.Jpeg, image.Data.ToArray());
        Assert.Equal(ChatResponseFormat.Json, fast.Requests[0].Options!.ResponseFormat);
    }

    [Fact]
    public async Task Detect_NoFastVisionModel_UsesStandard()
    {
        _router.Vision[ModelTier.Standard] = new FakeChatClient().ThenText("""{"failure": false, "reason": "first layer looks fine"}""");

        Result<PrintVerdict> verdict = await new VlmPrintFailureDetector(_router).DetectAsync(_snapshot, Ct);

        Assert.False(verdict.Value.Failure);
    }

    [Fact]
    public async Task Detect_NoVisionModel_NoVerdict()
    {
        Result<PrintVerdict> verdict = await new VlmPrintFailureDetector(_router).DetectAsync(_snapshot, Ct);

        Assert.True(verdict.IsFailure);
    }

    [Theory]
    [InlineData("```json\n{\"failure\": true, \"reason\": \"blob on the nozzle\"}\n```", true)]
    [InlineData("Yes, it failed.", null)]
    [InlineData("{\"failure\": \"yes\", \"reason\": \"x\"}", null)]
    [InlineData("{\"reason\": \"x\"}", null)]
    [InlineData("[true]", null)]
    public void Parse_OnlyStrictVerdicts(string text, bool? failure)
    {
        Result<PrintVerdict> verdict = VlmPrintFailureDetector.Parse(text);

        Assert.Equal(failure, verdict.IsSuccess ? verdict.Value.Failure : null);
        if (failure is null)
            Assert.Equal("printer.verdict_invalid", verdict.Error.Code);
    }
}
