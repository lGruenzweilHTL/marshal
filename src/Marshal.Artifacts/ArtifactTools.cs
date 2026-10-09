using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using Marshal.Abstractions;
using Marshal.Abstractions.Artifacts;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Tools;

namespace Marshal.Artifacts;

/// <summary>The <c>artifacts.*</c> tools (a built-in source).</summary>
public sealed class ArtifactToolSource(IArtifactStore store, IArtifactLinks links, ArtifactDisplays displays) : IToolSource
{
    private readonly IReadOnlyList<ITool> _tools = [new ListTool(store), new ShowTool(store, links, displays)];

    public string Name => "artifacts";

    public bool IsBuiltIn => true;

    public Task<IReadOnlyList<ITool>> GetToolsAsync(CancellationToken cancellationToken) => Task.FromResult(_tools);

    /// <summary>Most matches <c>artifacts.list</c> returns.</summary>
    internal const int ListLimit = 50;

    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private static ToolDescriptor Descriptor(string name, string description, string schema) => new()
    {
        Id = new ToolId("artifacts", name),
        Description = description,
        ParametersSchema = JsonDocument.Parse(schema).RootElement.Clone(),
        DefaultTier = RiskTier.Safe,
    };

    private static string? Text(JsonElement arguments, string property) =>
        arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static Error Invalid(string message) => new("artifacts.invalid_arguments", message, ErrorType.Validation);

    // Names can come from mail attachments or executor output, so the list is third-party text (taint "artifact").
    private sealed class ListTool(IArtifactStore store) : ITool
    {
        public ToolDescriptor Descriptor { get; } = ArtifactToolSource.Descriptor("list",
            "Lists the user's artifacts (files such as 3D models, images, code, documents), newest first, at most 50.",
            """
            {"type":"object","properties":{
              "kind":{"type":"string","enum":["image","svg","video","model3d","code","diff","pdf","markdown","html","other"]},
              "source":{"type":"string","enum":["upload","executor","printer","mail","github"]},
              "query":{"type":"string","description":"Part of the file name."}}}
            """) with { UntrustedOutput = true, TaintKind = "artifact" };

        public async Task<Result<ToolOutput>> InvokeAsync(ToolInvocation invocation, CancellationToken cancellationToken)
        {
            JsonElement arguments = invocation.Arguments;
            ArtifactKind? kind = null;
            if (Text(arguments, "kind") is { } kindText)
            {
                if (!Enum.TryParse(kindText, ignoreCase: true, out ArtifactKind parsed))
                    return Invalid($"Unknown kind '{kindText}'.");
                kind = parsed;
            }

            ArtifactSource? source = null;
            if (Text(arguments, "source") is { } sourceText)
            {
                if (!Enum.TryParse(sourceText, ignoreCase: true, out ArtifactSource parsed))
                    return Invalid($"Unknown source '{sourceText}'.");
                source = parsed;
            }

            IReadOnlyList<ArtifactRecord> found = await store.ListAsync(new ArtifactQuery(kind, source, Text(arguments, "query"), ListLimit), cancellationToken);
            return new ToolOutput(JsonSerializer.Serialize(
                found.Select(artifact => new
                {
                    id = artifact.Id.ToString(),
                    name = artifact.Name,
                    kind = artifact.Kind,
                    source = artifact.Source,
                    size = artifact.Size,
                    createdAt = artifact.CreatedAt.ToString("O", CultureInfo.InvariantCulture),
                }),
                _json));
        }
    }

    // Opens an artifact in the web chat, or plays its media rendition on the display in the satellite's room. The
    // display comes from the satellite profile only; the model cannot pick another screen.
    private sealed class ShowTool(IArtifactStore store, IArtifactLinks links, ArtifactDisplays displays) : ITool
    {
        public ToolDescriptor Descriptor { get; } = ArtifactToolSource.Descriptor("show",
            "Shows an artifact (by id from artifacts_list) to the user: in the chat in web conversations, on the screen in the room in voice conversations. Code, diffs and Markdown show as pages there; pass page for the next one.",
            """
            {"type":"object","properties":{
              "id":{"type":"string","description":"Artifact id from artifacts_list."},
              "page":{"type":"integer","minimum":1,"description":"Page on a screen, default 1."}},"required":["id"]}
            """);

        public ValueTask<bool> IsAvailableAsync(MarshalExecutionContext context, CancellationToken cancellationToken) =>
            ValueTask.FromResult(context.Source == SessionSource.Web || displays.For(context) is not null);

        public async Task<Result<ToolOutput>> InvokeAsync(ToolInvocation invocation, CancellationToken cancellationToken)
        {
            if (!ArtifactId.TryParse(Text(invocation.Arguments, "id"), null, out ArtifactId id))
                return new Error("artifacts.not_found", "There is no artifact with that id.", ErrorType.NotFound);

            if (invocation.Context.Source != SessionSource.Web)
                return await ShowOnDisplayAsync(id, invocation, cancellationToken);

            Result<ArtifactRecord> artifact = await store.GetAsync(id, cancellationToken);
            if (artifact.IsFailure)
                return artifact.Error;

            ArtifactRecord shown = artifact.Value;
            var reference = new ArtifactRef(shown.Id, shown.Name, shown.Kind, links.Content(shown.Id));
            return new ToolOutput($"Shown to the user: {shown.Name} ({shown.Kind.ToString().ToLowerInvariant()}).") { Artifacts = [reference] };
        }

        private async Task<Result<ToolOutput>> ShowOnDisplayAsync(ArtifactId id, ToolInvocation invocation, CancellationToken cancellationToken)
        {
            if (displays.For(invocation.Context) is not { } target)
                return new Error("artifacts.no_display", "There is no screen in this room.", ErrorType.Validation);

            int page = 1;
            if (invocation.Arguments.ValueKind == JsonValueKind.Object && invocation.Arguments.TryGetProperty("page", out JsonElement value) && value.ValueKind != JsonValueKind.Null
                && !value.TryGetInt32(out page))
            {
                return Invalid("page must be a whole number.");
            }

            Result<ArtifactRendition> rendition = await displays.Renditions.DescribeAsync(id, cancellationToken);
            if (rendition.IsFailure)
                return rendition.Error;

            ArtifactRendition shown = rendition.Value;
            if (page < 1 || page > shown.Pages)
                return SkiaArtifactRenditions.PageError(page, shown.Pages);

            string url = displays.Links.Create(id, page, shown.Extension);
            Result played = await target.Player.PlayAsync(target.Display, url, shown.DisplayType, cancellationToken);
            if (played.IsFailure)
                return played.Error;

            return new ToolOutput(string.Create(CultureInfo.InvariantCulture,
                $"Showing {shown.Artifact.Name} on the display {target.Display.Name}, page {page} of {shown.Pages}."));
        }
    }
}
