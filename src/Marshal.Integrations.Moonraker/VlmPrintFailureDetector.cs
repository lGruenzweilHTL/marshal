using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Models;
using Marshal.Abstractions.Printers;
using Microsoft.Extensions.AI;

namespace Marshal.Integrations.Moonraker;

/// <summary>
/// <see cref="IPrintFailureDetector"/> with a vision model (<c>fast</c>, else <c>standard</c>): the snapshot and a
/// fixed prompt asking for a strict JSON verdict. Anything but <c>{"failure": bool, "reason": string}</c> is no
/// verdict, never a positive one.
/// </summary>
public sealed class VlmPrintFailureDetector(IModelRouter router) : IPrintFailureDetector
{
    private const string Prompt = """
        You check a webcam image of a running 3D print for a failed print.
        A failure is only something clearly visible: spaghetti (loose strands of extruded plastic), the part detached
        from the bed or knocked over, a large blob of plastic around the nozzle, or the nozzle printing into the air.
        A dark, blurry or unclear image, an early first layer or an empty bed at the start is not a failure.
        Answer with only this JSON object and nothing else:
        {"failure": true or false, "reason": "what you see, at most 12 words"}
        """;

    private const int MaxReasonLength = 200;

    public async Task<Result<PrintVerdict>> DetectAsync(PrinterSnapshot snapshot, CancellationToken cancellationToken)
    {
        Result<RoutedModel> model = router.Route(new ModelRequest(ModelTier.Fast, Vision: true));
        if (model.IsFailure)
            model = router.Route(new ModelRequest(ModelTier.Standard, Vision: true));
        if (model.IsFailure)
            return model.Error;

        ChatResponse response = await model.Value.Client.GetResponseAsync(
            [
                new ChatMessage(ChatRole.System, Prompt),
                new ChatMessage(ChatRole.User, [new TextContent("Is this print failing?"), new DataContent(snapshot.Jpeg, "image/jpeg")]),
            ],
            new ChatOptions { ResponseFormat = ChatResponseFormat.Json, Temperature = 0 },
            cancellationToken);
        return Parse(response.Text);
    }

    internal static Result<PrintVerdict> Parse(string text)
    {
        string json = text.Trim();
        if (json.StartsWith("```", StringComparison.Ordinal))
        {
            int start = json.IndexOf('{', StringComparison.Ordinal);
            int end = json.LastIndexOf('}');
            json = start >= 0 && end > start ? json[start..(end + 1)] : json;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("failure", out JsonElement failure) && failure.ValueKind is JsonValueKind.True or JsonValueKind.False
                && root.TryGetProperty("reason", out JsonElement reason) && reason.ValueKind == JsonValueKind.String)
            {
                string said = reason.GetString()!.Trim();
                return new PrintVerdict(failure.GetBoolean(), said.Length <= MaxReasonLength ? said : said[..MaxReasonLength]);
            }
        }
        catch (JsonException)
        {
        }

        return new Error("printer.verdict_invalid", "The vision model gave no valid verdict.", ErrorType.Failure);
    }
}
