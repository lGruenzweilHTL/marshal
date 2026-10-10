using System.Numerics;
using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Artifacts;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Models;
using Marshal.Abstractions.Runs;
using Marshal.Abstractions.Tools;
using Marshal.Testing;
using Microsoft.Extensions.AI;
using SkiaSharp;

namespace Marshal.Artifacts.Tests.Models;

public sealed class ArtifactInspectTests
{
    private readonly FakeArtifactStore _store = new();
    private readonly VisionRouter _router = new();
    private readonly InMemoryRunRecorder _recorder = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Descriptor_SafeUntrustedWithArtifactTaint()
    {
        ToolDescriptor inspect = (await ToolAsync()).Descriptor;

        Assert.Equal((RiskTier.Safe, true, "artifact"), (inspect.DefaultTier, inspect.UntrustedOutput, inspect.TaintKind));
    }

    [Fact]
    public async Task HiddenWithoutVisionModel_StandardFallbackShows()
    {
        Assert.False(await (await ToolAsync()).IsAvailableAsync(TestExecutionContext.Voice(), Ct));

        _router.Vision[ModelTier.Standard] = new FakeChatClient();

        Assert.True(await (await ToolAsync()).IsAvailableAsync(TestExecutionContext.Voice(), Ct));
    }

    [Fact]
    public async Task WithoutInspector_NoInspectTool()
    {
        var source = new ArtifactToolSource(_store, new NoLinks(), new ArtifactDisplays(new SkiaArtifactRenditions(_store), new NoDisplayLinks(), new NoDisplays(), []));

        Assert.DoesNotContain(await source.GetToolsAsync(Ct), tool => tool.Descriptor.Id.Name == "inspect");
    }

    [Fact]
    public async Task ModelSheet_SentWithQuestion_AnswerReturned()
    {
        FakeChatClient vision = new FakeChatClient().ThenText("Yes, one hole on each side.");
        _router.Vision[ModelTier.Fast] = vision;
        ArtifactRecord model = await AddAsync("bracket.stl", MeshFixtures.BinaryStl(MeshFixtures.Box(Vector3.Zero, new Vector3(40, 20, 10))));

        Result<ToolOutput> answer = await InvokeAsync(new { id = model.Id.ToString(), question = "Are the mounting holes on both sides?", page = 2 }, TestExecutionContext.Voice());

        Assert.True(answer.IsSuccess, answer.IsFailure ? answer.Error.Message : null);
        Assert.Contains("Yes, one hole on each side.", answer.Value.Content, StringComparison.Ordinal);
        Assert.Contains("page 2 of 2", answer.Value.Content, StringComparison.Ordinal);
        ChatMessage user = vision.Requests[0].Messages.Single(message => message.Role == ChatRole.User);
        Assert.Equal("Are the mounting holes on both sides?", user.Contents.OfType<TextContent>().Single().Text);
        DataContent image = user.Contents.OfType<DataContent>().Single();
        using var sent = SKBitmap.Decode(image.Data.ToArray());
        Assert.Equal((1568, 882), (sent.Width, sent.Height));
        Assert.Contains("data", vision.Requests[0].SystemPrompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SmallImage_SentAsIs()
    {
        _router.Vision[ModelTier.Standard] = new FakeChatClient().ThenText("A red square.");
        ArtifactRecord photo = await AddAsync("photo.png", Png(200, 100));

        await InvokeAsync(new { id = photo.Id.ToString(), question = "What is it?" }, TestExecutionContext.Text());

        DataContent image = _router.Vision[ModelTier.Standard].Requests[0].Messages.SelectMany(message => message.Contents).OfType<DataContent>().Single();
        using var sent = SKBitmap.Decode(image.Data.ToArray());
        Assert.Equal((200, 100), (sent.Width, sent.Height));
    }

    [Fact]
    public async Task Video_NoRendition_NoModelCall()
    {
        FakeChatClient vision = new();
        _router.Vision[ModelTier.Standard] = vision;
        ArtifactRecord clip = await AddAsync("clip.mp4", [0, 0, 0, 0x18, 0x66, 0x74, 0x79, 0x70]);

        Result<ToolOutput> answer = await InvokeAsync(new { id = clip.Id.ToString(), question = "?" }, TestExecutionContext.Text());

        Assert.Equal("artifacts.no_rendition", answer.Error.Code);
        Assert.Empty(vision.Requests);
    }

    [Fact]
    public async Task PageOutOfRange_Invalid()
    {
        _router.Vision[ModelTier.Standard] = new FakeChatClient();
        ArtifactRecord photo = await AddAsync("photo.png", Png(10, 10));

        Assert.Equal("artifacts.invalid_arguments", (await InvokeAsync(new { id = photo.Id.ToString(), question = "?", page = 2 }, TestExecutionContext.Text())).Error.Code);
    }

    [Fact]
    public async Task QuestionAndId_Required()
    {
        ITool tool = await ToolAsync();

        Assert.Equal("artifacts.invalid_arguments", tool.Validate(JsonSerializer.SerializeToElement(new { id = ArtifactId.New().ToString() })).Error.Code);
        Assert.Equal("artifacts.invalid_arguments", tool.Validate(JsonSerializer.SerializeToElement(new { question = "?" })).Error.Code);
    }

    [Fact]
    public async Task RecordsTheModelCall()
    {
        _router.Vision[ModelTier.Standard] = new FakeChatClient().ThenText("fine");
        ArtifactRecord photo = await AddAsync("photo.png", Png(10, 10));
        RunId run = await _recorder.StartRunAsync(new RunStart(TestExecutionContext.Text(), "test"), Ct);

        await InvokeAsync(new { id = photo.Id.ToString(), question = "?" }, TestExecutionContext.Text() with { RunId = run });

        RunStepRecord step = Assert.Single(_recorder.OnlyRun.Steps);
        Assert.Equal((RunStepKind.ModelCall, "standard-vision", "fine"), (step.Kind, step.Model, step.Content));
    }

    private async Task<Result<ToolOutput>> InvokeAsync(object arguments, MarshalExecutionContext context) =>
        await (await ToolAsync()).InvokeAsync(new ToolInvocation(JsonSerializer.SerializeToElement(arguments), context), Ct);

    private async Task<ITool> ToolAsync()
    {
        var renditions = new SkiaArtifactRenditions(_store);
        var inspector = new ArtifactInspector(renditions, _router, new FakeTemplateCatalog(), _recorder, new ZeroCost(), TimeProvider.System);
        var source = new ArtifactToolSource(_store, new NoLinks(), new ArtifactDisplays(renditions, new NoDisplayLinks(), new NoDisplays(), []), inspector);
        return (await source.GetToolsAsync(Ct)).Single(tool => tool.Descriptor.Id.Name == "inspect");
    }

    private async Task<ArtifactRecord> AddAsync(string name, byte[] content) =>
        (await _store.CreateAsync(new ArtifactDraft(name, ArtifactSource.Upload), new MemoryStream(content), Ct)).Value;

    private static byte[] Png(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(SKColors.Red);
        using SKData data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private sealed class VisionRouter : IModelRouter
    {
        public Dictionary<ModelTier, FakeChatClient> Vision { get; } = [];

        public Result<RoutedModel> Route(ModelRequest request) =>
            request.Vision && Vision.TryGetValue(request.Tier, out FakeChatClient? client)
                ? new RoutedModel($"{request.Tier.ToString().ToLowerInvariant()}-vision", request.Tier, client)
                : new Error("model.not_available", "No model.", ErrorType.Unavailable);
    }

    private sealed class ZeroCost : ICostCalculator
    {
        public decimal Calculate(string modelKey, UsageDetails? usage) => 0;
    }

    private sealed class NoLinks : IArtifactLinks
    {
        public string Content(ArtifactId id) => $"/content/{id}";

        public string SignedContent(ArtifactId id) => $"/signed/{id}";
    }

    private sealed class NoDisplayLinks : Abstractions.Artifacts.IDisplayLinks
    {
        public string Create(ArtifactId id, int page, string extension) => "";
    }

    private sealed class NoDisplays : Abstractions.Displays.IDisplayDirectory
    {
        public Abstractions.Displays.DisplaySurface? ForSatellite(string? satelliteId) => null;
    }
}
