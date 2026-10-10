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
public sealed class ArtifactToolSource(IArtifactStore store, IArtifactLinks links, ArtifactDisplays displays, ArtifactInspector? inspector = null) : IToolSource
{
    private readonly IReadOnlyList<ITool> _tools = inspector is null
        ? [new ListTool(store), new ShowTool(store, links, displays)]
        : [new ListTool(store), new ShowTool(store, links, displays), new InspectTool(inspector)];

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

    // Sends one rendition page to a vision model. The answer describes a picture that can contain third-party text
    // (mail attachments, executor output), so it is untrusted like the list.
    private sealed class InspectTool(ArtifactInspector inspector) : ITool
    {
        public ToolDescriptor Descriptor { get; } = ArtifactToolSource.Descriptor("inspect",
            "Looks at an artifact (by id from artifacts_list) and answers a question about it: a 3D model (page 1 one view, page 2 front, right, top and perspective views), an image, or a page of code, a diff or Markdown. Good for checking a model's shape; not for exact measurements.",
            """
            {"type":"object","properties":{
              "id":{"type":"string","description":"Artifact id from artifacts_list."},
              "question":{"type":"string","description":"What to look for."},
              "page":{"type":"integer","minimum":1,"description":"Page of the rendition, default 1."}},"required":["id","question"]}
            """) with { UntrustedOutput = true, TaintKind = "artifact" };

        public ValueTask<bool> IsAvailableAsync(MarshalExecutionContext context, CancellationToken cancellationToken) =>
            ValueTask.FromResult(inspector.IsAvailable(context));

        public Result Validate(JsonElement arguments) => Parse(arguments) is { IsFailure: true } parsed ? parsed.Error : Result.Success();

        public async Task<Result<ToolOutput>> InvokeAsync(ToolInvocation invocation, CancellationToken cancellationToken)
        {
            Result<(ArtifactId Id, string Question, int Page)> parsed = Parse(invocation.Arguments);
            if (parsed.IsFailure)
                return parsed.Error;

            (ArtifactId id, string question, int page) = parsed.Value;
            return await inspector.InspectAsync(id, page, question, invocation.Context, cancellationToken);
        }

        private static Result<(ArtifactId Id, string Question, int Page)> Parse(JsonElement arguments)
        {
            if (!ArtifactId.TryParse(Text(arguments, "id"), null, out ArtifactId id))
                return Invalid("id must be an artifact id from artifacts_list.");
            if (Text(arguments, "question")?.Trim() is not { Length: > 0 } question)
                return Invalid("question is required.");

            int page = 1;
            if (arguments.TryGetProperty("page", out JsonElement value) && value.ValueKind != JsonValueKind.Null && (!value.TryGetInt32(out page) || page < 1))
                return Invalid("page must be a whole number from 1.");
            return (id, question, page);
        }
    }
}
