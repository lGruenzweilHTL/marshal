using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Guard;
using Marshal.Abstractions.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Marshal.Core.Guard;

/// <summary>
/// Fast-tier judge with structured output (JSON enum), temperature 0 and a short timeout. Anything but a clean
/// verdict is <see cref="JudgeVerdict.Failed"/>, which drops the action.
/// </summary>
public sealed partial class LlmConfirmationJudge(IModelRouter router, IOptions<ConfirmationOptions> options, TimeProvider timeProvider, ILogger<LlmConfirmationJudge> logger) : IConfirmationJudge
{
    private const string SystemPrompt = """
        You judge whether a user's reply confirms a pending action.
        Answer only with JSON: {"verdict": "positive" | "neutral" | "negative"}.
        - positive: the reply clearly confirms the action.
        - negative: the reply clearly rejects or cancels the action.
        - neutral: anything else, including a different request or a change of topic.
        If you are not confident, answer neutral.
        The reply may be in German or English.
        """;

    private static readonly ChatResponseFormat _format = ChatResponseFormat.ForJsonSchema(
        JsonDocument.Parse("""
            {"type":"object","properties":{"verdict":{"type":"string","enum":["positive","neutral","negative"]}},"required":["verdict"],"additionalProperties":false}
            """).RootElement.Clone(),
        "confirmation_verdict",
        "Whether the reply confirms the pending action.");

    public async Task<JudgeVerdict> JudgeAsync(string actionSummary, string userReply, string language, CancellationToken cancellationToken)
    {
        Result<RoutedModel> model = router.Route(new ModelRequest(ModelTier.Fast));
        if (model.IsFailure)
        {
            LogNoModel(logger);
            return JudgeVerdict.Failed;
        }

        using var timeout = new CancellationTokenSource(options.Value.JudgeTimeout, timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            ChatResponse response = await model.Value.Client.GetResponseAsync(
                [
                    new ChatMessage(ChatRole.System, SystemPrompt),
                    new ChatMessage(ChatRole.User, $"Pending action: {actionSummary}\nUser reply: {userReply}"),
                ],
                new ChatOptions { Temperature = 0, ResponseFormat = _format },
                linked.Token);
            return Parse(response.Text);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            LogTimeout(logger);
            return JudgeVerdict.Failed;
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or JsonException or IOException)
        {
            LogFailed(logger, exception.GetType().Name);
            return JudgeVerdict.Failed;
        }
    }

    internal static JudgeVerdict Parse(string? text)
    {
        string trimmed = text?.Trim() ?? "";
        if (trimmed.StartsWith('{'))
        {
            try
            {
                using var document = JsonDocument.Parse(trimmed);
                trimmed = document.RootElement.TryGetProperty("verdict", out JsonElement verdict) && verdict.ValueKind == JsonValueKind.String
                    ? verdict.GetString() ?? ""
                    : "";
            }
            catch (JsonException)
            {
                return JudgeVerdict.Failed;
            }
        }

        return trimmed.ToLowerInvariant() switch
        {
            "positive" => JudgeVerdict.Positive,
            "neutral" => JudgeVerdict.Neutral,
            "negative" => JudgeVerdict.Negative,
            _ => JudgeVerdict.Failed,
        };
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Confirmation judge unavailable: no fast model configured")]
    private static partial void LogNoModel(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Confirmation judge timed out; dropping the pending action")]
    private static partial void LogTimeout(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Confirmation judge failed with {ExceptionType}; dropping the pending action")]
    private static partial void LogFailed(ILogger logger, string exceptionType);
}
